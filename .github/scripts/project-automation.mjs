import fs from 'node:fs';
import path from 'node:path';

const token = process.env.PROJECT_TOKEN;
const repositoryFullName = process.env.GITHUB_REPOSITORY;
const eventName = process.env.GITHUB_EVENT_NAME;
const eventPath = process.env.GITHUB_EVENT_PATH;
const manualIssueNumber = process.env.MANUAL_ISSUE_NUMBER?.trim();

if (!token) {
  throw new Error('PROJECT_TOKEN is not available. Add it as a repository Actions secret.');
}

if (!repositoryFullName) {
  throw new Error('GITHUB_REPOSITORY is not available.');
}

const configPath = path.resolve('.github/project-config.json');
const config = JSON.parse(fs.readFileSync(configPath, 'utf8'));
const event = eventPath && fs.existsSync(eventPath)
  ? JSON.parse(fs.readFileSync(eventPath, 'utf8'))
  : {};

const [repositoryOwner, repositoryName] = repositoryFullName.split('/');

async function graphql(query, variables = {}) {
  const response = await fetch('https://api.github.com/graphql', {
    method: 'POST',
    headers: {
      Authorization: `Bearer ${token}`,
      'Content-Type': 'application/json',
      'User-Agent': 'AgenStart-Project-Automation'
    },
    body: JSON.stringify({ query, variables })
  });

  if (!response.ok) {
    throw new Error(`GitHub GraphQL HTTP ${response.status}: ${response.statusText}`);
  }

  const payload = await response.json();

  if (payload.errors?.length) {
    const messages = payload.errors.map((error) => error.message).join(' | ');
    throw new Error(`GitHub GraphQL error: ${messages}`);
  }

  return payload.data;
}

function normalize(value) {
  return String(value ?? '').trim().toLowerCase();
}

function parseProjectMetadata(body = '') {
  const match = body.match(/<!--\s*agenstart-project\s*([\s\S]*?)-->/i);
  if (!match) return {};

  const metadata = {};

  for (const rawLine of match[1].split('\n')) {
    const line = rawLine.trim();
    if (!line || line.startsWith('#')) continue;

    const separator = line.indexOf(':');
    if (separator < 1) continue;

    const key = line.slice(0, separator).trim();
    const value = line.slice(separator + 1).trim();

    if (['priority', 'workType', 'phase', 'size'].includes(key) && value) {
      metadata[key] = value;
    }
  }

  return metadata;
}

function inferWorkType(title = '') {
  for (const [prefix, workType] of Object.entries(config.workTypeByTitlePrefix ?? {})) {
    if (title.startsWith(prefix)) return workType;
  }
  return undefined;
}

function issueMetadata(issue) {
  const inferred = { workType: inferWorkType(issue.title) };
  const override = config.issueOverrides?.[String(issue.number)] ?? {};
  const embedded = parseProjectMetadata(issue.body ?? '');

  return Object.fromEntries(
    Object.entries({ ...inferred, ...override, ...embedded })
      .filter(([, value]) => value !== undefined && value !== null && value !== '')
  );
}

async function findProject() {
  const query = `
    query ProjectByOwner($login: String!) {
      user(login: $login) {
        projectsV2(first: 100) {
          nodes {
            id
            number
            title
            fields(first: 100) {
              nodes {
                __typename
                ... on ProjectV2Field {
                  id
                  name
                }
                ... on ProjectV2SingleSelectField {
                  id
                  name
                  options {
                    id
                    name
                  }
                }
                ... on ProjectV2IterationField {
                  id
                  name
                }
              }
            }
          }
        }
      }
      organization(login: $login) {
        projectsV2(first: 100) {
          nodes {
            id
            number
            title
            fields(first: 100) {
              nodes {
                __typename
                ... on ProjectV2Field {
                  id
                  name
                }
                ... on ProjectV2SingleSelectField {
                  id
                  name
                  options {
                    id
                    name
                  }
                }
                ... on ProjectV2IterationField {
                  id
                  name
                }
              }
            }
          }
        }
      }
    }
  `;

  const data = await graphql(query, { login: config.project.owner });
  const projects = [
    ...(data.user?.projectsV2?.nodes ?? []),
    ...(data.organization?.projectsV2?.nodes ?? [])
  ];

  const project = projects.find((candidate) => candidate.title === config.project.title);

  if (!project) {
    const visible = projects.map((candidate) => candidate.title).join(', ') || '(none visible)';
    throw new Error(
      `Project "${config.project.title}" was not found for ${config.project.owner}. Visible projects: ${visible}`
    );
  }

  console.log(`Resolved Project #${project.number}: ${project.title}`);
  return project;
}

async function findProjectItem(projectId, contentId) {
  const query = `
    query FindProjectItem($projectId: ID!, $after: String) {
      node(id: $projectId) {
        ... on ProjectV2 {
          items(first: 100, after: $after) {
            nodes {
              id
              content {
                ... on Issue { id }
                ... on PullRequest { id }
              }
            }
            pageInfo {
              hasNextPage
              endCursor
            }
          }
        }
      }
    }
  `;

  let after = null;

  do {
    const data = await graphql(query, { projectId, after });
    const connection = data.node?.items;

    if (!connection) {
      throw new Error('Unable to read Project items. Check PROJECT_TOKEN project permissions.');
    }

    const match = connection.nodes.find((item) => item.content?.id === contentId);
    if (match) return match.id;

    if (!connection.pageInfo.hasNextPage) break;
    after = connection.pageInfo.endCursor;
  } while (after);

  return null;
}

async function ensureProjectItem(projectId, contentId) {
  const existingItemId = await findProjectItem(projectId, contentId);
  if (existingItemId) {
    return { itemId: existingItemId, added: false };
  }

  const mutation = `
    mutation AddProjectItem($projectId: ID!, $contentId: ID!) {
      addProjectV2ItemById(input: { projectId: $projectId, contentId: $contentId }) {
        item { id }
      }
    }
  `;

  const data = await graphql(mutation, { projectId, contentId });
  const itemId = data.addProjectV2ItemById?.item?.id;

  if (!itemId) throw new Error('GitHub did not return the newly added Project item id.');
  return { itemId, added: true };
}

function findSingleSelectField(project, configuredName) {
  return project.fields.nodes.find(
    (field) => field?.name === configuredName && Array.isArray(field.options)
  );
}

async function setSingleSelect(project, itemId, configuredFieldName, optionName) {
  if (!configuredFieldName || !optionName) return;

  const field = findSingleSelectField(project, configuredFieldName);
  if (!field) {
    console.warn(`Skipping missing/non-single-select Project field: ${configuredFieldName}`);
    return;
  }

  const option = field.options.find((candidate) => normalize(candidate.name) === normalize(optionName));
  if (!option) {
    console.warn(
      `Skipping ${configuredFieldName}="${optionName}" because that option does not exist. ` +
      `Available: ${field.options.map((candidate) => candidate.name).join(', ')}`
    );
    return;
  }

  const mutation = `
    mutation SetProjectField(
      $projectId: ID!,
      $itemId: ID!,
      $fieldId: ID!,
      $optionId: String!
    ) {
      updateProjectV2ItemFieldValue(
        input: {
          projectId: $projectId,
          itemId: $itemId,
          fieldId: $fieldId,
          value: { singleSelectOptionId: $optionId }
        }
      ) {
        projectV2Item { id }
      }
    }
  `;

  await graphql(mutation, {
    projectId: project.id,
    itemId,
    fieldId: field.id,
    optionId: option.id
  });

  console.log(`Set ${configuredFieldName} → ${option.name}`);
}

async function applyIssueFields(project, issue, itemId, status) {
  const metadata = issueMetadata(issue);

  await setSingleSelect(project, itemId, config.fields.status, status);
  await setSingleSelect(project, itemId, config.fields.priority, metadata.priority);
  await setSingleSelect(project, itemId, config.fields.workType, metadata.workType);
  await setSingleSelect(project, itemId, config.fields.phase, metadata.phase);
  await setSingleSelect(project, itemId, config.fields.size, metadata.size);
}

async function loadIssue(number) {
  const query = `
    query RepositoryIssue($owner: String!, $name: String!, $number: Int!) {
      repository(owner: $owner, name: $name) {
        issue(number: $number) {
          id
          number
          title
          body
          state
        }
      }
    }
  `;

  const data = await graphql(query, {
    owner: repositoryOwner,
    name: repositoryName,
    number: Number(number)
  });

  const issue = data.repository?.issue;
  if (!issue) throw new Error(`Issue #${number} was not found in ${repositoryFullName}.`);
  return issue;
}

async function closingIssuesForPullRequest(pullRequestId) {
  const query = `
    query PullRequestClosingIssues($id: ID!) {
      node(id: $id) {
        ... on PullRequest {
          closingIssuesReferences(first: 50) {
            nodes {
              id
              number
              title
              body
              state
              repository { nameWithOwner }
            }
          }
        }
      }
    }
  `;

  const data = await graphql(query, { id: pullRequestId });
  return (data.node?.closingIssuesReferences?.nodes ?? [])
    .filter((issue) => issue.repository?.nameWithOwner === repositoryFullName);
}

function issueStatusForAction(action) {
  switch (action) {
    case 'opened': return config.statusTransitions.issueOpened;
    case 'reopened': return config.statusTransitions.issueReopened;
    case 'closed': return config.statusTransitions.issueClosed;
    default: return undefined;
  }
}

async function handleIssueEvent(project) {
  const issue = event.issue;
  if (!issue?.node_id) throw new Error('Issue event does not contain issue.node_id.');

  const { itemId } = await ensureProjectItem(project.id, issue.node_id);
  const status = issueStatusForAction(event.action);
  await applyIssueFields(project, issue, itemId, status);
  console.log(`Synced Issue #${issue.number}: ${issue.title}`);
}

async function handleManualIssue(project) {
  const issue = await loadIssue(manualIssueNumber);
  const { itemId, added } = await ensureProjectItem(project.id, issue.id);
  const status = added
    ? (issue.state === 'CLOSED' ? config.statusTransitions.issueClosed : config.statusTransitions.issueOpened)
    : undefined;

  await applyIssueFields(project, issue, itemId, status);
  console.log(`Manually synced Issue #${issue.number}: ${issue.title}`);
}

async function handlePullRequestEvent(project) {
  const pullRequest = event.pull_request;
  if (!pullRequest?.node_id) throw new Error('Pull request event does not contain pull_request.node_id.');

  const issues = await closingIssuesForPullRequest(pullRequest.node_id);
  if (!issues.length) {
    console.log('No linked closing issues found for this pull request. Nothing to update.');
    return;
  }

  let targetStatus;

  if (event.action === 'closed' && pullRequest.merged) {
    targetStatus = config.statusTransitions.pullRequestMerged;
  } else if (['opened', 'reopened'].includes(event.action) && pullRequest.draft) {
    targetStatus = config.statusTransitions.draftPullRequest;
  } else if (['opened', 'reopened', 'ready_for_review'].includes(event.action)) {
    targetStatus = config.statusTransitions.pullRequestReady;
  } else {
    console.log(`No status transition configured for PR action ${event.action}.`);
    return;
  }

  for (const issue of issues) {
    const { itemId } = await ensureProjectItem(project.id, issue.id);
    await applyIssueFields(project, issue, itemId, targetStatus);
    console.log(`PR lifecycle moved Issue #${issue.number} → ${targetStatus}`);
  }
}

const project = await findProject();

if (eventName === 'workflow_dispatch' && manualIssueNumber) {
  await handleManualIssue(project);
} else if (eventName === 'issues') {
  await handleIssueEvent(project);
} else if (eventName === 'pull_request') {
  await handlePullRequestEvent(project);
} else {
  console.log(`Unsupported event ${eventName}; nothing to do.`);
}
