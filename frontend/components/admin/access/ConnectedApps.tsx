"use client";

import { useState } from "react";
import type { OAuthGrantDto } from "@/lib/api/generated";
import { formatRelative } from "@/lib/admin/useApiTokens";
import { useConnectedApps } from "@/lib/admin/useConnectedApps";
import { Badge } from "../ui/Badge";
import { Button } from "../ui/Button";
import { EmptyState } from "../ui/EmptyState";
import { Panel, PanelHeader } from "../ui/Panel";
import { Toggle } from "../ui/Toggle";

/**
 * Apps connected through "Sign in with BlogRAG" — an AI assistant's MCP connector. Each one
 * renews its own access, so the list is the author's one place to see who can write for
 * them, widen or narrow what each may do, and cut any of them off.
 */
export function ConnectedApps() {
  const { apps, state, busy, disconnect, setMode } = useConnectedApps();

  // Nothing worth a panel on a deployment that has OAuth switched off.
  if (state === "error") return null;

  return (
    <Panel className="flex flex-col gap-4 p-5 sm:p-6">
      <PanelHeader
        icon="share"
        title="Connected apps"
        description="AI assistants you approved. By default they write drafts only: create and edit drafts, add pictures and publish when you ask. Switch on full editing to also let one edit live posts, replace artwork, unpublish and delete. Changes apply on the app's next request; disconnecting ends its access at once."
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
            <AppRow
              key={app.id}
              app={app}
              busy={busy}
              onSetMode={(fullEditing) => void setMode(app.id!, app.clientName ?? "app", fullEditing)}
              onDisconnect={() => void disconnect(app.id!, app.clientName ?? "app")}
            />
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

function AppRow({
  app,
  busy,
  onSetMode,
  onDisconnect,
}: {
  app: OAuthGrantDto;
  busy: boolean;
  onSetMode: (fullEditing: boolean) => void;
  onDisconnect: () => void;
}) {
  const [confirming, setConfirming] = useState(false);
  const fullEditing = app.fullEditing ?? false;
  const name = app.clientName ?? "app";

  return (
    <li className="flex flex-col gap-3 px-4 py-3">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="min-w-0">
          <div className="flex items-center gap-2">
            <span className="truncate text-[13.5px] font-medium text-warm-black">{name}</span>
            <Badge tone={fullEditing ? "accent" : "outline"}>{fullEditing ? "full editing" : "drafts only"}</Badge>
          </div>
          <p className="mt-0.5 text-xs text-warm-slate">
            Connected {formatRelative(app.createdAt)}
            {app.lastUsedAt ? ` · last active ${formatRelative(app.lastUsedAt)}` : ""}
          </p>
        </div>

        <div className="flex flex-wrap items-center gap-2">
          <Toggle
            id={`full-editing-${app.id}`}
            label="Full editing"
            checked={fullEditing || confirming}
            disabled={busy}
            onChange={(next) => {
              // Widening asks first; narrowing is always safe, so it applies at once.
              if (next) setConfirming(true);
              else if (confirming) setConfirming(false);
              else onSetMode(false);
            }}
          />
          <Button variant="danger" disabled={busy} onClick={onDisconnect}>
            Disconnect
          </Button>
        </div>
      </div>

      {confirming ? (
        <div className="flex flex-wrap items-center justify-end gap-2 rounded border border-warm-border bg-warm-sunken px-3 py-2">
          <span className="mr-auto text-xs leading-relaxed text-warm-slate">
            {name} will be able to edit live posts, replace artwork, unpublish, and delete posts and
            files for good. You can switch this off at any time.
          </span>
          <Button variant="ghost" onClick={() => setConfirming(false)}>
            Cancel
          </Button>
          <Button
            autoFocus
            variant="danger"
            aria-label={`Allow full editing for ${name}`}
            onClick={() => {
              setConfirming(false);
              onSetMode(true);
            }}
          >
            Allow full editing
          </Button>
        </div>
      ) : null}
    </li>
  );
}
