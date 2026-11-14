"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { ResponseError, type OAuthGrantDto } from "@/lib/api/generated";
import { describeError, oauthConsentApi } from "./client";
import { useAuth } from "./useAuth";
import { useToast } from "./useToast";

/**
 * The apps an author has approved through OAuth — an AI assistant's connector, typically.
 * Unlike a token there is no secret to show and nothing to rotate: the app refreshes its own
 * access, so the author only ever switches an app's mode or disconnects it.
 */
export function useConnectedApps() {
  const { session } = useAuth();
  const { showToast } = useToast();

  const authorId = session?.authorId ?? null;

  const [apps, setApps] = useState<OAuthGrantDto[]>([]);
  const [state, setState] = useState<"loading" | "ready" | "unavailable" | "error">("loading");
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
    } catch (error) {
      if (marker !== requestToken.current) return;

      // NOTE: a deployment without OAuth configured answers 503, and the panel hides. Any
      // other failure keeps it on screen with a retry, so a real outage is not mistaken for
      // "OAuth is off" and the author can still reach a connected app to disconnect it.
      setState(error instanceof ResponseError && error.response.status === 503 ? "unavailable" : "error");
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

  const setMode = useCallback(
    async (id: number, name: string, fullEditing: boolean) => {
      setBusy(true);

      try {
        const updated = await oauthConsentApi.oAuthConsentSetGrantMode({
          id,
          grantModeDto: { fullEditing },
        });
        setApps((current) => current.map((app) => (app.id === id ? updated : app)));
        showToast(
          fullEditing ? `“${name}” can now edit and delete posts` : `“${name}” is back to drafts only`,
          "info",
        );
      } catch (error) {
        showToast(await describeError(error, "Could not change that app's access."), "error");
      } finally {
        setBusy(false);
      }
    },
    [showToast],
  );

  const reload = useCallback(async () => {
    setState("loading");
    await load();
  }, [load]);

  return { apps: apps.filter((app) => app.isActive), state, busy, disconnect, setMode, reload };
}
