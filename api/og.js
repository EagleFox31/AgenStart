import React from "react";
import { ImageResponse } from "@vercel/og";

const e = React.createElement;

const palette = {
  bg: "#071B25",
  panel: "#0C2733",
  panelSoft: "#103440",
  text: "#F5FAFC",
  muted: "#A9BCC5",
  cyan: "#26D8D1",
  green: "#78D6B2",
  border: "rgba(152, 211, 218, 0.18)",
};

function Badge({ children }) {
  return e(
    "div",
    {
      style: {
        display: "flex",
        alignItems: "center",
        padding: "10px 16px",
        border: `1px solid ${palette.border}`,
        borderRadius: 999,
        background: "rgba(10, 38, 49, 0.78)",
        color: palette.muted,
        fontSize: 18,
      },
    },
    children,
  );
}

function AppRow({ name, state, accent }) {
  return e(
    "div",
    {
      style: {
        display: "flex",
        alignItems: "center",
        justifyContent: "space-between",
        padding: "15px 18px",
        borderTop: `1px solid ${palette.border}`,
        fontSize: 18,
      },
    },
    e(
      "div",
      { style: { display: "flex", alignItems: "center", gap: 12 } },
      e("div", {
        style: {
          width: 14,
          height: 14,
          borderRadius: 4,
          background: accent,
          boxShadow: `0 0 18px ${accent}`,
        },
      }),
      e("span", { style: { color: palette.text, fontWeight: 700 } }, name),
    ),
    e(
      "span",
      {
        style: {
          color: state === "Installed" ? palette.green : palette.cyan,
          fontSize: 15,
        },
      },
      state,
    ),
  );
}

export default function handler() {
  return new ImageResponse(
    e(
      "div",
      {
        style: {
          width: "1200px",
          height: "630px",
          display: "flex",
          position: "relative",
          overflow: "hidden",
          padding: "54px 58px",
          background:
            "radial-gradient(circle at 82% 20%, rgba(38,216,209,.18), transparent 30%), radial-gradient(circle at 18% 90%, rgba(21,122,142,.20), transparent 35%), #071B25",
          color: palette.text,
          fontFamily: "Arial, Helvetica, sans-serif",
        },
      },

      e("div", {
        style: {
          position: "absolute",
          width: 520,
          height: 520,
          border: "1px solid rgba(38,216,209,.16)",
          borderRadius: "50%",
          right: -60,
          top: -280,
        },
      }),
      e("div", {
        style: {
          position: "absolute",
          width: 360,
          height: 360,
          border: "1px solid rgba(38,216,209,.12)",
          borderRadius: "50%",
          right: 80,
          top: -190,
        },
      }),

      e(
        "div",
        {
          style: {
            width: "49%",
            display: "flex",
            flexDirection: "column",
            position: "relative",
            zIndex: 2,
          },
        },
        e(
          "div",
          { style: { display: "flex", alignItems: "center", gap: 14 } },
          e(
            "div",
            {
              style: {
                width: 42,
                height: 42,
                borderRadius: 12,
                background: palette.cyan,
                color: palette.bg,
                display: "flex",
                alignItems: "center",
                justifyContent: "center",
                fontSize: 24,
                fontWeight: 900,
              },
            },
            "A",
          ),
          e(
            "div",
            { style: { display: "flex", flexDirection: "column" } },
            e(
              "div",
              { style: { fontSize: 27, fontWeight: 800, letterSpacing: -0.5 } },
              "Agen",
              e("span", { style: { color: palette.cyan } }, "Start"),
            ),
            e(
              "div",
              { style: { fontSize: 13, color: palette.muted, letterSpacing: 1.5 } },
              "BY AGENSTUDIO",
            ),
          ),
        ),

        e(
          "div",
          {
            style: {
              marginTop: 44,
              fontSize: 14,
              letterSpacing: 3,
              color: "#9BC0C9",
            },
          },
          "WINDOWS 10/11  •  LOCAL-FIRST  •  NO ACCOUNT REQUIRED",
        ),

        e(
          "div",
          {
            style: {
              marginTop: 18,
              maxWidth: 545,
              fontSize: 52,
              lineHeight: 1.03,
              fontWeight: 850,
              letterSpacing: -2.4,
              display: "flex",
              flexDirection: "column",
            },
          },
          e("span", null, "Your next PC setup"),
          e("span", null, "should already know"),
          e("span", { style: { color: palette.cyan } }, "what you need."),
        ),

        e(
          "div",
          {
            style: {
              marginTop: 20,
              maxWidth: 520,
              color: palette.muted,
              fontSize: 20,
              lineHeight: 1.4,
            },
          },
          "Smart recommendations, trusted installs and reproducible Windows setups — without the scavenger hunt.",
        ),

        e(
          "div",
          { style: { display: "flex", gap: 12, marginTop: 30 } },
          e(Badge, null, "Smart recommendations"),
          e(Badge, null, "Trusted installs"),
        ),
      ),

      e(
        "div",
        {
          style: {
            width: "51%",
            display: "flex",
            alignItems: "center",
            justifyContent: "flex-end",
            position: "relative",
            zIndex: 2,
          },
        },
        e(
          "div",
          {
            style: {
              width: 545,
              border: `1px solid ${palette.border}`,
              borderRadius: 26,
              background:
                "linear-gradient(145deg, rgba(15,48,61,.98), rgba(7,27,37,.98))",
              boxShadow: "0 28px 70px rgba(0,0,0,.32)",
              overflow: "hidden",
              transform: "rotate(-1.4deg)",
            },
          },
          e(
            "div",
            {
              style: {
                display: "flex",
                alignItems: "center",
                justifyContent: "space-between",
                padding: "18px 22px",
                borderBottom: `1px solid ${palette.border}`,
              },
            },
            e(
              "div",
              { style: { display: "flex", alignItems: "center", gap: 10 } },
              e("div", {
                style: {
                  width: 14,
                  height: 14,
                  borderRadius: 4,
                  background: palette.cyan,
                },
              }),
              e("span", { style: { fontWeight: 800, fontSize: 19 } }, "AgenStart"),
            ),
            e(
              "div",
              {
                style: {
                  display: "flex",
                  alignItems: "center",
                  gap: 8,
                  color: "#6F929C",
                  fontSize: 16,
                },
              },
              "●  ●  ●",
            ),
          ),

          e(
            "div",
            { style: { padding: "26px 28px 12px", display: "flex", flexDirection: "column" } },
            e(
              "div",
              { style: { color: palette.cyan, fontSize: 13, letterSpacing: 2 } },
              "SMART SETUP",
            ),
            e(
              "div",
              {
                style: {
                  display: "flex",
                  justifyContent: "space-between",
                  alignItems: "center",
                  marginTop: 7,
                },
              },
              e(
                "div",
                { style: { fontSize: 30, fontWeight: 800, letterSpacing: -0.8 } },
                "Development workstation",
              ),
              e(
                "div",
                {
                  style: {
                    display: "flex",
                    padding: "8px 12px",
                    borderRadius: 999,
                    background: "rgba(120,214,178,.12)",
                    color: palette.green,
                    fontSize: 14,
                  },
                },
                "●  Machine ready",
              ),
            ),
            e(
              "div",
              { style: { color: palette.muted, marginTop: 7, fontSize: 16 } },
              "14 apps fit this machine + profile",
            ),

            e(
              "div",
              {
                style: {
                  display: "flex",
                  gap: 10,
                  marginTop: 22,
                },
              },
              ...[
                ["OS", "Windows 11 Pro"],
                ["MEMORY", "32 GB RAM"],
                ["ARCH", "x64"],
              ].map(([label, value]) =>
                e(
                  "div",
                  {
                    key: label,
                    style: {
                      flex: 1,
                      display: "flex",
                      flexDirection: "column",
                      padding: "14px",
                      borderRadius: 13,
                      background: "rgba(14,51,64,.85)",
                      border: `1px solid ${palette.border}`,
                    },
                  },
                  e(
                    "span",
                    { style: { color: "#82A9B3", fontSize: 11, letterSpacing: 1.4 } },
                    label,
                  ),
                  e(
                    "span",
                    { style: { fontSize: 17, fontWeight: 750, marginTop: 7 } },
                    value,
                  ),
                ),
              ),
            ),

            e(
              "div",
              {
                style: {
                  marginTop: 20,
                  display: "flex",
                  flexDirection: "column",
                  border: `1px solid ${palette.border}`,
                  borderRadius: 16,
                  overflow: "hidden",
                },
              },
              e(AppRow, { name: "Git", state: "Installed", accent: "#F05033" }),
              e(AppRow, { name: "Visual Studio Code", state: "Ready", accent: "#29A9EA" }),
              e(AppRow, { name: "Docker Desktop", state: "Smart match", accent: "#2496ED" }),
              e(AppRow, { name: "Windows Terminal", state: "Skip", accent: "#B8C6CB" }),
            ),
          ),
        ),
      ),
    ),
    {
      width: 1200,
      height: 630,
      headers: {
        "Cache-Control": "public, max-age=86400, s-maxage=86400",
      },
    },
  );
}
