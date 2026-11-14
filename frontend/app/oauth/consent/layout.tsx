import type { Metadata } from "next";
import { AdminProviders } from "@/components/admin/AdminProviders";

/** Reached only from an app's sign-in link, so it is kept out of the index like the studio. */
export const metadata: Metadata = {
  title: "Connect an app",
  robots: { index: false, follow: false },
};

export default function ConsentLayout({ children }: LayoutProps<"/oauth/consent">) {
  return (
    <AdminProviders>
      <div className="studio-theme flex flex-1 flex-col">{children}</div>
    </AdminProviders>
  );
}
