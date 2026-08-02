# AgriERP — Frontend (Step 5a)

Next.js shell, authentication, dashboard and all six master screens. Billing and transaction screens follow in 5b.

> **Later renames.** `/products` is now `/items` and `/categories` is
> `/item-subgroups`, and the item form is one group-driven panel rather than the
> three-tab layout described below. See `docs/08-item-groups.md`. This document
> reflects the screens as built at step 5.

---

## 1. Running it

```powershell
# terminal 1 - API
dotnet run --project Backend/AgriERP.API --urls http://localhost:5215

# terminal 2 - web
cd Frontend
npm run dev        # or: npm run build && npm run start
```

Open <http://localhost:3000>. First sign-in **`admin` / `Admin@123`**; the app forces a password change before anything else is reachable.

`Frontend/.env.local` points the client at the API:

```
NEXT_PUBLIC_API_URL=http://localhost:5215/api
```

---

## 2. Stack

| Concern | Choice |
|---|---|
| Framework | Next.js 15 (App Router), React 19, TypeScript |
| Styling | Tailwind CSS 3 + ShadCN UI (new-york) |
| Forms | React Hook Form + Zod |
| Server state | TanStack Query |
| HTTP | Axios with a JWT + refresh interceptor |
| Theme | next-themes (light / dark / system) |
| Charts | Recharts |
| Export | SheetJS (Excel), jsPDF + autotable (PDF), browser print |

**11 routes, 62 source files.** Production build compiles clean; `tsc --noEmit` passes with no errors.

---

## 3. Screens

| Route | What it does |
|---|---|
| `/login` | Sign in. The form is in the pre-rendered HTML, not behind Suspense. |
| `/change-password` | Forced on first run; also reachable from the user menu. |
| `/dashboard` | 8 stat tiles, 12-month trend chart, category donut, recent bills, top sellers |
| `/products` | Full CRUD, 3-tab form, batch list, category/company/stock filters |
| `/categories` | CRUD with one-level parent nesting |
| `/companies` | CRUD with GSTIN, address and contact |
| `/suppliers` | CRUD with payment terms, credit limit, bank details |
| `/customers` | CRUD with village, mobile, price type, credit terms |
| `/units` | CRUD for units of measure |

Every list has server-side paging, debounced search, sortable columns, filters, and Excel / PDF / Print.

---

## 4. Decisions worth knowing

### 4.1 Refresh is serialised

When an access token expires, several queries fail at once. The API **rotates** refresh tokens and treats reuse of a rotated one as theft — so if each failed call fired its own refresh, the second would revoke the whole session and log the user out mid-task. The client queues them: the first 401 refreshes, everything else waits on that one promise.

### 4.2 Permissions gate the UI, the API enforces them

`useAuth().can("Product.Create")` hides buttons and menu entries, reading the permission claims from the JWT. It is a courtesy, not a control — every endpoint re-checks the same code server-side. Menu sections with nothing visible collapse entirely rather than rendering an empty heading.

### 4.3 The nav lists only routes that exist

A menu entry pointing at an unbuilt page teaches the user the app is unreliable. Stock, purchase, sales, payments and reports join the sidebar when their screens land in 5b.

### 4.4 Exports are client-side, and page-scoped

"Export" gives exactly what is on screen, filters included — which is what the user expects. It also means it exports the **current page**, not the whole table. Raising rows-per-page first (the API caps at 200) covers most cases; a true full-dataset export belongs on the server where it can stream.

### 4.5 Charts follow the validated palette

Series colours are a fixed categorical order, never cycled, defined once in `globals.css` and consumed by Recharts as `var(--chart-n)`. Light and dark are **separate validated sets**, not an automatic flip.

The palette was checked with the validator rather than by eye — lightness band, chroma floor, colour-blind separation, normal-vision floor and contrast, in both modes. Three light-mode hues fall below 3:1 on white, which is the documented relief case, so **every chart ships a legend, direct value labels and a "Data" toggle that swaps the plot for a table**. Colour never carries meaning alone; the same rule is why stock status renders as a word, not just a tint.

One y-axis on the trend chart. Sales, purchase and profit are all rupees; a second axis would invite the reader to compare two different scales by height.

The donut caps at five categories plus "Other". Past the sixth slot there is no next hue to reach for — the palette is a fixed order, not a generator.

---

## 5. Verification

Five suites, all green:

```
Transactions API HTTP   49 passed, 0 failed
Masters API HTTP        32 passed, 0 failed
Frontend integration    30 passed, 0 failed   tests/api/frontend-smoke.ps1
Persistence (EF model)   8 passed, 0 failed
SQL smoke               10 passed, 0 failed
```

`frontend-smoke.ps1` checks every page is served, that the login form is in the static HTML, and that **the CORS preflight succeeds from `http://localhost:3000`** — the one failure a server-side test can never catch, because the browser sends it and a server-to-server call does not.

### Visual check

`Frontend/scripts/visual-check.mjs` drives a real headless browser: signs in, screenshots every screen in light and dark at desktop and mobile, and fails on a blank page, a console error, missing expected text, a chart that drew no geometry, or a body that scrolls horizontally.

```powershell
cd Frontend
npm install --no-save playwright && npx playwright install chromium --only-shell
node scripts/visual-check.mjs ./shots
```

It needs `sec.Users.MustChangePassword` cleared for the admin first (and restored after) — the guard correctly blocks everything else until the default password is changed, and patching localStorage does not get past it because `AuthProvider` re-fetches `/auth/me` and the server's answer wins.

Final run: **no problems**, 6 chart paths drawn in both modes, 16 category rows, 390px body width on mobile.

---

## 6. Problems this step surfaced

**Dark mode did not work at all.** `defaultTheme="light"` alongside `enableSystem` pins next-themes to light and never consults the OS, so `enableSystem` was dead config and a machine set to dark still got a white screen. Only the screenshots caught it — the build, the types and the API checks were all perfectly happy. Now `defaultTheme="system"`.

**The shadcn CLI wrote `oklch()` colours into a Tailwind v3 project.** The v3 config wraps every colour in `hsl(var(--x))`, which cannot read an oklch literal — every themed colour would have rendered transparent. Rewritten as HSL triplets.

**The CLI's own `npm install` pruned six packages** that had been installed minutes earlier — lucide-react, TanStack Query, axios, recharts among them — and never created `@/lib/utils`. Caught by the first typecheck.

**The login form was not in the pre-rendered HTML.** `useSearchParams` forces the whole page into a Suspense boundary, so the static shell was a bare spinner and the form only appeared after hydration. The return path is now read from `window.location` at submit time instead — and only same-site paths are honoured, since an absolute URL there would be an open redirect off the login page.

**Sub-categories interleaved with parents in the category list** — "Vegetable Seeds" appearing between "Insecticide" and "Pesticide", because parents and children number their display order independently. The default sort now puts roots first.

**A `form.reset` was running in a render body** on the Companies page, which is a side effect during render and re-triggers the render that caused it. Moved into an effect.

Also: `npm` forbids capitals in package names, so the folder is `AgriERP.Web` (matching the solution layout) while `package.json` says `agrierp-web`.

---

## 7. Next: Step 5b

- **Billing screen** — product type-ahead, barcode scan, FEFO batch display, live GST, multi-mode payment, print
- **Purchase entry** — line grid with batch and expiry, landed-cost preview, post/cancel
- **Stock** — ledger, adjustments, transfers, opening stock
- **Payments** — collection screen with open-bill allocation
- **Reports** — the report endpoints with date pickers, charts and export

Then step 6 (testing) and step 7 (deployment).
