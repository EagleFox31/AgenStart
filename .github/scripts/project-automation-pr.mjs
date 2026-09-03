// pull_request_target is used so PROJECT_TOKEN is never exposed to untrusted PR code.
// The main automation script expects the logical event name `pull_request`.
process.env.GITHUB_EVENT_NAME = 'pull_request';
await import('./project-automation.mjs');
