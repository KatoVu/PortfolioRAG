"use client";

import { useEffect, useState } from "react";
import type { AuthorizeRequestDto, ConsentDescriptionDto } from "@/lib/api/generated";
import { describeError, oauthConsentApi } from "@/lib/admin/client";
import { useAuth } from "@/lib/admin/useAuth";
import { SignInForm } from "@/components/admin/SignInForm";
import { Button } from "@/components/admin/ui/Button";
import { Icon } from "@/components/admin/ui/Icon";
import { Panel } from "@/components/admin/ui/Panel";

type Load =
  | { state: "loading" }
  | { state: "ready"; description: ConsentDescriptionDto }
  | { state: "error"; message: string };

/**
 * Asks the signed-in author whether an app — an AI assistant's connector, in practice — may
 * write for them, and how much: drafts only, or full editing. Either answer sends the
 * browser back to the app. The mode can be switched later from the studio.
 *
 * Signing in happens on this screen rather than by a detour through /admin, so the request
 * in the URL is never lost between the two.
 */
export function ConsentScreen({ request }: { request: AuthorizeRequestDto }) {
  const { status, session } = useAuth();

  if (status === "loading") {
    return (
      <main className="flex min-h-dvh items-center justify-center">
        <p className="font-mono text-xs text-warm-slate">Loading…</p>
      </main>
    );
  }

  if (status !== "authenticated") return <SignInForm />;

  if (session && !session.emailConfirmed) {
    return (
      <Frame>
        <Notice
          title="Confirm your email first"
          body="Connecting an app is a write to your account, and the API refuses writes until your address is confirmed. Confirm it from the studio, then start connecting again from the app."
        />
      </Frame>
    );
  }

  return <Consent request={request} />;
}

function Consent({ request }: { request: AuthorizeRequestDto }) {
  const [load, setLoad] = useState<Load>({ state: "loading" });
  const [deciding, setDeciding] = useState<null | "approve" | "deny">(null);
  const [failure, setFailure] = useState<string | null>(null);
  const [fullEditing, setFullEditing] = useState(false);

  useEffect(() => {
    let current = true;

    void (async () => {
      try {
        const description = await oauthConsentApi.oAuthConsentDescribe({ authorizeRequestDto: request });
        if (current) setLoad({ state: "ready", description });
      } catch (error) {
        const message = await describeError(error, "This connection request is not valid.");
        if (current) setLoad({ state: "error", message });
      }
    })();

    return () => {
      current = false;
    };
  }, [request]);

  const decide = async (approve: boolean) => {
    setDeciding(approve ? "approve" : "deny");
    setFailure(null);

    try {
      const result = await oauthConsentApi.oAuthConsentDecide({
        consentDecisionDto: { ...request, approve, fullEditing: approve && fullEditing },
      });

      // NOTE: a full navigation, not the router: the target is the app's own redirect —
      // often a loopback address on this machine — and nothing of the studio belongs there.
      window.location.assign(result.redirectUrl!);
    } catch (error) {
      setFailure(await describeError(error, "Could not record your answer."));
      setDeciding(null);
    }
  };

  if (load.state === "loading") {
    return (
      <Frame>
        <p className="font-mono text-xs text-warm-slate">Checking the request…</p>
      </Frame>
    );
  }

  if (load.state === "error") {
    return (
      <Frame>
        <Notice
          title="This link cannot be used"
          body={`${load.message} Go back to the app you were connecting and start again.`}
        />
      </Frame>
    );
  }

  const { description } = load;

  return (
    <Frame>
      <div className="flex flex-col gap-5">
        <div>
          <p className="font-mono text-[11px] tracking-wide text-warm-slate uppercase">Connect an app</p>
          <h1 className="mt-1 font-serif text-2xl font-medium text-warm-black">
            {description.clientName} wants to write posts for you
          </h1>
          <p className="mt-2 text-[13.5px] leading-relaxed text-warm-slate">
            The name is what the app calls itself — nothing has checked it. Only approve if you
            started connecting an app just now.
          </p>
        </div>

        <fieldset className="flex flex-col gap-2">
          <legend className="mb-2 text-[13px] font-semibold text-warm-black">How much can it do?</legend>

          <ModeOption
            checked={!fullEditing}
            onSelect={() => setFullEditing(false)}
            title="Drafts only"
            hint="Recommended. Nothing it does can remove or change what is already live."
          >
            <List icon="check-circle" title="It will be able to" items={description.allows ?? []} />
            <List icon="error" title="It will never be able to" items={description.neverAllows ?? []} />
          </ModeOption>

          <ModeOption
            checked={fullEditing}
            onSelect={() => setFullEditing(true)}
            title="Full editing"
            hint="Everything above, plus:"
          >
            <List icon="error" title="It will also be able to" items={description.fullEditingAllows ?? []} />
          </ModeOption>
        </fieldset>

        <p className="border-t border-warm-border/60 pt-3 text-xs leading-relaxed text-warm-slate">
          Approving sends you back to {description.redirectTarget}. Either way it can never touch
          your profile, timeline, credentials or account. You can switch its mode or disconnect
          it at any time from the studio&apos;s Access tab.
        </p>

        {failure ? (
          <p role="alert" className="text-[13px] text-warm-danger">
            {failure}
          </p>
        ) : null}

        <div className="flex flex-wrap justify-end gap-2">
          <Button busy={deciding === "deny"} disabled={deciding !== null} onClick={() => void decide(false)}>
            Deny
          </Button>
          <Button
            variant="primary"
            icon="check-circle"
            busy={deciding === "approve"}
            disabled={deciding !== null}
            onClick={() => void decide(true)}
          >
            Approve
          </Button>
        </div>
      </div>
    </Frame>
  );
}

function ModeOption({
  checked,
  onSelect,
  title,
  hint,
  children,
}: {
  checked: boolean;
  onSelect: () => void;
  title: string;
  hint: string;
  children: React.ReactNode;
}) {
  return (
    <label
      className={
        checked
          ? "flex cursor-pointer flex-col gap-3 rounded border border-warm-black bg-warm-surface p-3"
          : "flex cursor-pointer flex-col gap-3 rounded border border-warm-border bg-warm-sunken p-3"
      }
    >
      <span className="flex items-start gap-2.5">
        <input
          type="radio"
          name="mode"
          checked={checked}
          onChange={onSelect}
          className="mt-[3px] accent-warm-black"
        />
        <span>
          <span className="block text-[13.5px] font-medium text-warm-black">{title}</span>
          <span className="block text-xs text-warm-slate">{hint}</span>
        </span>
      </span>
      {checked ? <div className="flex flex-col gap-3 pl-6">{children}</div> : null}
    </label>
  );
}

function Frame({ children }: { children: React.ReactNode }) {
  return (
    <main className="flex min-h-dvh items-center justify-center px-6 py-16">
      <Panel className="w-full max-w-md p-6">{children}</Panel>
    </main>
  );
}

function Notice({ title, body }: { title: string; body: string }) {
  return (
    <div className="flex flex-col gap-2">
      <h1 className="font-serif text-xl font-medium text-warm-black">{title}</h1>
      <p className="text-[13.5px] leading-relaxed text-warm-slate">{body}</p>
    </div>
  );
}

function List({
  icon,
  title,
  items,
}: {
  icon: "check-circle" | "error";
  title: string;
  items: string[];
}) {
  return (
    <div>
      <h2 className="mb-2 text-[13px] font-semibold text-warm-black">{title}</h2>
      <ul className="flex flex-col gap-1.5">
        {items.map((item) => (
          <li key={item} className="flex items-start gap-2 text-[13.5px] leading-relaxed text-warm-slate">
            <Icon
              name={icon}
              className={icon === "error" ? "mt-[3px] text-[15px] text-warm-danger" : "mt-[3px] text-[15px] text-warm-success"}
            />
            <span>{item}</span>
          </li>
        ))}
      </ul>
    </div>
  );
}
