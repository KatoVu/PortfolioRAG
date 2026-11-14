import { ConsentScreen } from "@/components/oauth/ConsentScreen";
import type { AuthorizeRequestDto } from "@/lib/api/generated";

/**
 * Where /oauth/authorize on the API sends the browser: the author approves an app here, in
 * the studio, because the studio is what holds their session.
 *
 * The query is read on the server and handed down as plain props. The API validates every
 * field again on both calls the screen makes, so nothing here is trusted.
 */
export default async function ConsentPage({ searchParams }: PageProps<"/oauth/consent">) {
  const query = await searchParams;
  const read = (key: string) => {
    const value = query[key];
    return typeof value === "string" ? value : undefined;
  };

  const request: AuthorizeRequestDto = {
    clientId: read("client_id"),
    redirectUri: read("redirect_uri"),
    responseType: read("response_type"),
    codeChallenge: read("code_challenge"),
    codeChallengeMethod: read("code_challenge_method"),
    state: read("state"),
    scope: read("scope"),
    resource: read("resource"),
  };

  return <ConsentScreen request={request} />;
}
