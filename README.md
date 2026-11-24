# Nudge: a mini freelance marketplace (ASP.NET Core + SQL Server)

A small Upwork-style web app. Clients post jobs, freelancers apply, the client hires one freelancer and marks the job as completed, and the site owner (admin) takes a commission on each job. Data is stored in a SQL database through Entity Framework Core. The site name is set in `appsettings.json` (`Marketplace:SiteName`).

## About this project

This is a guided-learning starter project. It was prepared with AI assistance, and the code and this README were not written from scratch by the repository owner. It is meant as a base to study, run, and then modify. It is not a production system, and it is not evidence of finished skill by itself.

**Status:** the owner is still working through the code. Do not read this repository as a claim that every part is already understood.

**No real money:** there are no payment rails. The app acts out a full escrow story for learning: the client "pays" (by InstaPay or a mobile wallet, as a label) when hiring, the site owner "holds" the money, and it is "released" to the freelancer minus the commission when the freelancer marks the work finished. All of it is numbers in the database and admin dashboard (bookkeeping only). Nothing is charged, held, or paid out for real. Real payments would need a payment provider, legal and tax work, and a proper security review.

**Before presenting this as your own skill, do the following:**

1. Run it and read every file. Be able to say what each file does without looking.
2. Change something real without help: add a job category, add a review/rating after completion, or add a messaging page or a public freelancer profile page.
3. Replace `EnsureCreated` with EF Core migrations, and write automated tests.
4. Be able to answer the questions in the interview section below in your own words.
5. When you describe it to others, say it started from an AI-assisted base and say what you changed yourself.

## Features

**Roles**

| Role | Who | Can do |
|---|---|---|
| Admin | the site owner | admin dashboard with commission totals, suspend/reactivate users, cancel any open or in-progress job |
| Client | signs up on the site | post jobs, review applications, hire one freelancer and pay (demo, held by the owner), cancel own job (held payment refunded) |
| Freelancer | signs up on the site | build a portfolio (links and/or uploaded PDF, PNG or JPG files, up to 10 items), browse and search jobs, apply once per job with a cover letter and price (needs at least one portfolio item), mark hired work finished to trigger the automatic payout, see own applications and earnings |

**Portfolio:** freelancers add portfolio items on the My portfolio page. Clients see a freelancer's items next to their application, so they can judge the work before hiring. Uploads are checked on the server: allowed types only (PDF, PNG, JPG), content must match the type (first bytes), max 5 MB, saved under a random name in `App_Data/uploads` (outside `wwwroot`, ignored by git). Files are only served through `Portfolio/File/{id}`, which checks that the viewer is the owner, the admin, or a client whose job that freelancer applied to. Links must start with http:// or https://.

**Application approval (two steps):** every application starts as *waiting for the site owner's approval*. The admin reviews it in Admin > Review applications (cover letter, offer and portfolio are shown) and approves or rejects it. Clients only see approved applications for their jobs, and can only hire from those; the server enforces this, not just the page. Freelancers see where their application stands (waiting for the owner, approved and waiting for the client, accepted, declined, or not approved). When a client hires someone, every other application for that job is closed, including ones the admin had not reviewed yet.

**Job life cycle (demo escrow):** `Open` -> client accepts one application AND chooses how to pay (InstaPay or a mobile wallet) -> `InProgress` with the payment recorded as `Held` by the site owner -> the hired freelancer marks the work finished -> `Completed`, and the payout is released automatically: agreed price minus commission. A job can be `Cancelled` while open or in progress (no commission is kept, and a held payment is recorded as refunded to the client). The `Escrow` field on the job tracks where the demo money sits: `None`, `Held` or `Released`.

**Commission:** when the client accepts an application, the agreed price and commission rate are copied onto the job. Commission = price x rate / 100, rounded to 2 decimals (default rate 10%, set in `appsettings.json`). It counts as "pending" while the job is in progress and "earned" once the job is completed. The freelancer's dashboard shows earnings after commission. Example: agreed price 150.50 at 10% gives commission 15.05 and freelancer 135.45.

**Security basics**

- Passwords are not stored in plain text. They are hashed with ASP.NET Core `PasswordHasher` (PBKDF2 with a random salt and many iterations), which makes offline guessing slow but does not make weak passwords safe.
- Cookie login (HttpOnly, 30-minute sliding expiry). Role-based pages with `[Authorize(Roles = ...)]`.
- Every action re-checks on the server who is asking and what state the job is in (for example only the client who posted a job can hire for it). Hiding a button is not security.
- Suspended accounts are blocked at login and are logged out on their next request.
- Public sign-up can only create Client or Freelancer accounts. The admin account is created from configuration, never from the sign-up form.
- Unique email enforced by the database, one application per freelancer per job enforced by a unique index.
- Anti-forgery tokens on all forms, POST for every state change (including logout), and local-only return URLs after login.

## Tech stack

ASP.NET Core 8 (Razor Pages), C#, Entity Framework Core 8, SQL Server (LocalDB, Express, or Docker), HTML/CSS (no JavaScript framework).

## Project structure

```
Models/        User, Job, JobApplication, PortfolioItem, Roles
Data/          AppDbContext (tables, indexes, enum and money settings)
Services/      CommissionCalculator, ClaimsExtensions, PortfolioStorage (upload checks and saving)
Program.cs     setup: database, hashing, cookie auth + account check, admin seeding
Pages/Account  Register, Login, Logout, AccessDenied
Pages/Jobs     Index (browse/search), Create, Details (apply, accept, reject, complete, cancel)
Pages/Dashboard  client: my jobs / freelancer: my applications and earnings
Pages/Portfolio Manage (add/delete items), File (permission-checked download)
Pages/Admin    Index (stats + commission), Applications (approve/reject), Users, Jobs
wwwroot/css    site.css
```

## Setup

You need the [.NET 8 SDK](https://dotnet.microsoft.com/download) and a SQL Server.

### 1. Create the admin (owner) account

The owner account is created at start-up from configuration, so no password is stored in the repository. Use user-secrets (run these once in the project folder):

```
dotnet user-secrets init
dotnet user-secrets set "Admin:Email" "you@example.com"
dotnet user-secrets set "Admin:Password" "choose-a-strong-password"
```

Use your own email and a strong password. If `Admin:Email` is empty, no admin is created.

### 2a. SQL Server LocalDB (Windows, easiest)

The default connection string already points to LocalDB (it comes with Visual Studio):

```
dotnet run
```

### 2b. SQL Server in Docker (Windows, Mac, Linux)

```
docker run -e "ACCEPT_EULA=Y" -e "MSSQL_SA_PASSWORD=YourStrong!Passw0rd" -p 1433:1433 -d mcr.microsoft.com/mssql/server:2022-latest
dotnet user-secrets set "ConnectionStrings:SqlServer" "Server=localhost,1433;Database=MarketplaceDb;User Id=sa;Password=YourStrong!Passw0rd;TrustServerCertificate=True"
dotnet run
```

### 2c. SQLite (zero install, for a quick try only)

The main path of this project is SQL Server. To just see the app run:

```
dotnet run --Database:Provider=Sqlite
```

Tables are created automatically on first run (`EnsureCreated` in `Program.cs`). Open the URL printed in the terminal (http://localhost:5080 with the default profile). Log in with your admin account, then use two other browsers (or private windows) to sign up as a client and as a freelancer and try the full flow.

## Try the full flow

1. Sign up as a Client and post a job.
2. In another browser, sign up as a Freelancer, add a portfolio item (a link is enough), then apply with a price.
3. As the admin, open Review applications and approve it. (The client sees nothing until you do.)
4. As the client, open the job, look at the applicant's portfolio, choose a payment method (InstaPay or a wallet) and accept the application. The payment is now "held" by the owner.
5. As the freelancer, open the job and mark the work as finished. The payout (price minus commission) is released automatically.
6. Log in as the admin: the dashboard shows the commission as pending, then earned, and Transactions shows the held and released money.

## How to explain this in an interview

**30-second summary:** "It is a small freelance marketplace in ASP.NET Core with SQL Server. There are three roles: clients post jobs, freelancers apply, and the owner has an admin dashboard. Passwords are stored as salted PBKDF2 hashes, login uses a cookie, pages are protected by role, and the commission is calculated when a client accepts an application and counted as earned when the job is completed."

Be honest in an interview that this started from an AI-assisted base, and talk about what you ran, changed, and learned. Questions to be ready for:

- **Why hash passwords instead of encrypting them?** Hashing is one-way, so a leaked database does not directly reveal passwords. Encryption can be reversed with the key. Attackers who steal hashes can still guess passwords offline. Salt and many iterations make each guess slow, but short or common passwords can still be found.
- **How do roles work?** The role is stored on the user, copied into the login cookie as a claim, and `[Authorize(Roles = "Admin")]` checks it. A cookie event also re-checks the database on each request, so suspending a user or changing a role takes effect immediately.
- **How does the approval step work?** Each application has two separate fields: `Approval` (the owner's decision) and `Status` (the client's decision). The client's queries filter on `Approval == Approved`, so a client cannot see or hire an unapproved application even by sending the request by hand.
- **How do you stop a client from hiring on someone else's job?** The handler loads the job and compares `Job.ClientId` to the logged-in user id from the cookie. The id is never taken from the form. Buttons are also hidden in the page, but the server check is what protects the data.
- **Why is the commission copied onto the job?** If the rate changes later, old jobs keep the rate they were agreed at. Money history should not change silently.
- **How are uploads kept safe?** Extension allow-list, a check of the file's first bytes, a 5 MB limit, a random stored name (the user's file name is never used as a path), storage outside `wwwroot`, a permission check on every download, and `nosniff` so browsers do not guess the type. Files are not scanned for malware and they live on local disk, which a real deployment would replace with cloud storage and scanning.
- **Why decimal and not double for money?** `double` cannot represent many decimal amounts exactly. `decimal` is exact for money.
- **Why are the unique indexes needed as well as code checks?** The code check gives a friendly message. The index guarantees correctness when two requests race.
- **What is EF Core?** An ORM that maps C# classes to tables and turns LINQ into SQL.
- **Why `Restrict` on some foreign keys?** To stop deleting a user who still has jobs or applications, and to avoid SQL Server's "multiple cascade paths" error.
- **What would you add next?** Migrations, tests, email notifications, reviews, messaging, pagination, file uploads, and real payments through a payment provider (escrow, payouts, taxes).

## Limitations (honest list)

- Only tested on the SQLite option by the person who prepared it. The SQL Server option uses the standard EF Core provider but has not been run.
- Tested with scripted HTTP checks and screenshots only. There are no automated tests in the repository and no security review.
- No real payments, no email confirmation or password reset, no account lockout after failed logins.
- Uses `EnsureCreated`, so there are no migrations for changing the schema later.
- The admin's approval is final: there is no "undo", and no notification is sent to anyone (no email). Applications are one per freelancer per job and cannot be edited or withdrawn. A job has one hired freelancer.
- Validation is server-side only (no client-side scripts). Lists are limited to 50 jobs with no pagination.
- Uploaded files are stored on local disk and are not virus-scanned. Link contents are not checked at all. No image thumbnails or file preview for PDFs.
- Money amounts use a single demo currency label (`Marketplace:Currency`).

## Publish to GitHub

Create an empty repository named `sho8lana` on GitHub first (no README or .gitignore there). Then:

```
cd sho8lana
git init
git add .
git commit -m "Initial commit: ASP.NET Core freelance marketplace"
git branch -M main
git remote add origin https://github.com/FadyyNabil/sho8lana.git
git push -u origin main
```

Never commit passwords. The admin credentials belong in user-secrets, not in `appsettings.json`.
