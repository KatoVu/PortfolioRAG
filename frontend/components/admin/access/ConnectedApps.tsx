"use client";

import { formatRelative } from "@/lib/admin/useApiTokens";
import { useConnectedApps } from "@/lib/admin/useConnectedApps";
import { Badge } from "../ui/Badge";
import { Button } from "../ui/Button";
import { EmptyState } from "../ui/EmptyState";
import { Panel, PanelHeader } from "../ui/Panel";

/**
 * Apps connected through "Sign in with BlogRAG" — an AI assistant's MCP connector. Each one
 * holds a drafts-only token it renews by itself, so the list is the author's one place to
 * see who can write drafts for them and to cut any of them off.
 */
export function ConnectedApps() {
  const { apps, state, busy, disconnect } = useConnectedApps();

  // Nothing worth a panel on a deployment that has OAuth switched off.
  if (state === "error") return null;

  return (
    <Panel className="flex flex-col gap-4 p-5 sm:p-6">
      <PanelHeader
        icon="share"
        title="Connected apps"
        description="AI assistants you approved to write drafts for you. They can create and edit drafts, add pictures and publish when you ask — never delete, never touch a live post. Disconnecting ends their access at once."
        aside={
          <span className="rounded border border-warm-border bg-warm-sunken px-2 py-0.5 text-[13px] font-medium text-warm-black">
            {apps.length} connected
          </span>
        }
      />

      {state === "loading" ? (
        <div aria-hidden className="h-16 rounded bg-warm-sunken" />
      ) : apps.length > 0 ? (
        <ul className="divide-y divide-warm-border/60 overflow-hidden rounded border border-warm-border bg-warm-surface">
          {apps.map((app) => (
            <li key={app.id} className="flex flex-wrap items-center justify-between gap-3 px-4 py-3">
              <div className="min-w-0">
                <div className="flex items-center gap-2">
                  <span className="truncate text-[13.5px] font-medium text-warm-black">{app.clientName}</span>
                  <Badge tone="outline">drafts only</Badge>
                </div>
                <p className="mt-0.5 text-xs text-warm-slate">
                  Connected {formatRelative(app.createdAt)}
                  {app.lastUsedAt ? ` · last active ${formatRelative(app.lastUsedAt)}` : ""}
                </p>
              </div>
              <Button
                variant="danger"
                disabled={busy}
                onClick={() => void disconnect(app.id!, app.clientName ?? "app")}
              >
                Disconnect
              </Button>
            </li>
          ))}
        </ul>
      ) : (
        <EmptyState
          icon="share"
          title="No connected apps"
          description="Add the BlogRAG MCP server to an AI assistant and sign in from it to connect one."
        />
      )}
    </Panel>
  );
}
