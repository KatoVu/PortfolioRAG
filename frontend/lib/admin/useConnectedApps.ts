"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import type { OAuthGrantDto } from "@/lib/api/generated";
import { describeError, oauthConsentApi } from "./client";
import { useAuth } from "./useAuth";
import { useToast } from "./useToast";

/**
 * The apps an author has approved through OAuth — an AI assistant's connector, typically.
 * Unlike a token there is no secret to show and nothing to rotate: the app refreshes its own
 * access, so the only thing the author ever does here is disconnect one.
 */
export function useConnectedApps() {
  const { session } = useAuth();
  const { showToast } = useToast();

  const authorId = session?.authorId ?? null;

  const [apps, setApps] = useState<OAuthGrantDto[]>([]);
  const [state, setState] = useState<"loading" | "ready" | "error">("loading");
  const [busy, setBusy] = useState(false);

  // Drops the answer to a load that a later one has already overtaken.
  const requestToken = useRef(0);

  const load = useCallback(async () => {
    if (authorId === null) return;

    const marker = ++requestToken.current;

    try {
      const loaded = await oauthConsentApi.oAuthConsentListGrants();
      if (marker !== requestToken.current) return;

      setApps(loaded);
      setState("ready");
    } catch {
      if (marker !== requestToken.current) return;

      // NOTE: quiet on purpose. A deployment without OAuth configured answers 503 here,
      // and the tokens above are still worth showing; the section just stays empty.
      setState("error");
    }
  }, [authorId]);

  useEffect(() => {
    void (async () => {
      await load();
    })();
  }, [load]);

  const disconnect = useCallback(
    async (id: number, name: string) => {
      setBusy(true);

      try {
        await oauthConsentApi.oAuthConsentRevokeGrant({ id });
        await load();
        showToast(`Disconnected “${name}”`, "info");
      } catch (error) {
        showToast(await describeError(error, "Could not disconnect that app."), "error");
      } finally {
        setBusy(false);
      }
    },
    [load, showToast],
  );

  return { apps: apps.filter((app) => app.isActive), state, busy, disconnect };
}
