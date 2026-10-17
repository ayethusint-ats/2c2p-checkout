import type { Metadata } from "next";
import "./globals.css";

export const metadata: Metadata = {
  title: "Checkout",
  description: "Checkout with the 2C2P sandbox",
};

// The layout wraps every page: the centered card is defined once here.
// It's a server component (no "use client"), because it has no state or event handlers.
export default function RootLayout({ children }: LayoutProps<"/">) {
  return (
    <html lang="en">
      <body className="min-h-screen bg-slate-100 font-sans text-slate-900 antialiased">
        <main className="mx-auto w-full max-w-md px-4 py-10">
          <div className="rounded-xl bg-white p-6 shadow-sm ring-1 ring-slate-200">{children}</div>
        </main>
      </body>
    </html>
  );
}
