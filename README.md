# NightclubWorkforce

A full-stack workforce management platform built for nightclub and hospitality venues.  
**Stack:** Angular 21 (standalone components, signals) · ASP.NET Core 8 · Entity Framework Core · SignalR · PostgreSQL

---

## Table of Contents

- [Architecture](#architecture)
- [Current Functionalities](#current-functionalities)
  - [Staff App](#staff-app)
  - [Admin App](#admin-app)
  - [Backend API](#backend-api)
- [Portal Access Rules](#portal-access-rules)
- [Running Locally](#running-locally)
- [Building](#building)

---

## Architecture

```
nightclub-workforce/
├── backend/                         # ASP.NET Core 8 Web API
│   └── src/
│       ├── Api/Controllers/         # REST endpoints
│       ├── Application/             # CQRS handlers (MediatR)
│       ├── Domain/Entities/         # Core business entities
│       └── Infrastructure/          # EF Core, Hangfire jobs, SignalR hub, WebPush
├── projects/
│   ├── shared-lib/                  # Angular library shared by both apps
│   ├── staff-app/                   # Staff-facing PWA
│   └── admin-app/                   # Manager / Admin dashboard
└── nginx/                           # Reverse proxy config (Docker)
```

---

## Current Functionalities

### Staff App

#### Authentication & Account
| Feature | Description |
|---|---|
| Login | Staff ID + password login with portal enforcement (admin accounts blocked) |
| Magic Link QR Login | Scan a one-time QR/URL token to auto-login on mobile without typing credentials |
| Forgot Password | Self-service recovery request sent to manager |
| Set / Change Password | First-login forced change + ongoing self-service |
| Profile | View personal details, update phone number, toggle push notifications |
| Dark / Light Theme | Theme toggle in top bar; persists across sessions |

#### Clock In / Out
| Feature | Description |
|---|---|
| QR Scan Clock-In | Scan location QR code to start shift; validates roster entry for today |
| Roster Enforcement | Staff can only clock in at their scheduled location; unscheduled staff are blocked |
| Clock Out | End active shift from the app |
| Active Shift Status | Live display of current on-shift state, location and elapsed time |

#### My Schedule
| Feature | Description |
|---|---|
| Upcoming Shifts | View next 4 weeks of roster entries |
| Shift Confirmation | Confirm a scheduled shift before it occurs |

#### My Shifts
| Feature | Description |
|---|---|
| Shift History | Paginated list of all past shifts with hours and status |
| Pay Summary | Monthly earnings breakdown (hours × rate + OT multiplier + tips = gross pay) |
| Pay Slip | Printable pay statement for any month |

#### Leave
| Feature | Description |
|---|---|
| Apply for Leave | Submit sick / annual / unpaid leave with full or half-day period |
| Medical Certificate | Upload PDF/image cert for sick leave |
| Leave Balance | Remaining annual and sick day entitlement |
| Leave History | All requests with status (pending / approved / rejected) |
| Cancel Request | Withdraw a pending application |

#### Shift Swaps
| Feature | Description |
|---|---|
| Request Swap / Cover | Request to swap a rostered shift with a colleague, or ask for a cover |
| My Swap Requests | View all outgoing and incoming swap requests with their status |
| Respond to Swap | Accept or decline an incoming swap request from a colleague |

#### Announcements
| Feature | Description |
|---|---|
| Notice Feed | View announcements posted by management (banner on clock-in screen for unread items) |
| Read Receipts | Marking an announcement as read clears the banner |

#### Push Notifications (PWA)
| Feature | Description |
|---|---|
| Subscribe | Enable browser push so notifications arrive even when the app is closed |
| Unsubscribe | Revoke push permission from the profile page |
| Shift Alerts | Receive push when a shift is assigned, confirmed, or cancelled |
| Leave Alerts | Receive push when a leave request is approved or rejected |

---

### Admin App

#### Dashboard
| Feature | Description |
|---|---|
| Live KPIs | On-shift count, total staff, live hours, real-time labour cost |
| Open Shifts Badge | Amber warning card showing count of shifts with no clock-out (missed clock-outs) |
| Live Activity Feed | SignalR-powered stream of clock in/out events as they happen |

#### Staff Management
| Feature | Description |
|---|---|
| Staff List | Search / filter all staff by name, ID, or role |
| Register Staff | Create account with ID, roles, rate, DOB, contact details |
| Staff Detail | Full profile view including document links, shift summary, and performance stats |
| Performance Summary | Per-employee card: attendance rate, avg hours/shift, avg minutes late, OT shifts, leave used |
| Edit Staff | Update name, email, roles, hourly rate, contact |
| Activate / Deactivate | Toggle active status |
| Reset Password | Admin-generated reset with custom or auto password |
| Identity Documents | Upload IC and passport scans (PDF / JPG / PNG, 10 MB limit) |
| Generate Magic Link | One-time auto-login URL for onboarding staff on mobile |

#### Shift & Schedule Management
| Feature | Description |
|---|---|
| Shift View | All shifts across staff filtered by date range |
| Edit Shift | Adjust clock-in/out times, add tips, add notes (e.g. "late arrival") |
| Overtime Flag | Shifts > 8 hours automatically badged OT in amber |
| Schedule Grid | Weekly grid showing multiple shifts per cell (multi-location support) |
| Create / Edit Entry | Assign staff to location with date, time range, and status |
| Delete Entry | Remove roster entry with confirmation |
| Print Schedule | One-click print/PDF of the weekly roster grid (print-optimised CSS) |
| Bulk CSV Import | Upload a CSV to create many roster entries at once |
| Shift Templates | Save a named weekly pattern and apply it to any week with one click |

#### Shift Swaps (Admin)
| Feature | Description |
|---|---|
| Approval Queue | View all pending swap / cover requests across all staff |
| Filter by Status | Filter queue by pending / approved / rejected / cancelled |
| Approve / Reject | Admin review of a swap after both parties have responded |

#### Announcements
| Feature | Description |
|---|---|
| Compose Announcement | Post a notice with title, body, optional expiry, and target role filter |
| Manage Announcements | View, edit, deactivate, or delete all announcements |
| Read-Receipt Count | See how many staff have read each announcement |

#### Tips Pooling
| Feature | Description |
|---|---|
| Record Tips Pool | Enter total tips collected at a location for a given date |
| Auto-Split | Split equally or proportionally by hours worked across clocked-in staff |
| Distribute | Confirm distribution — individual tip amounts are saved to each shift |
| Pool History | View past distributions with per-staff allocations |

#### Overtime Rate Configuration
| Feature | Description |
|---|---|
| OT Rules | Define threshold hours and multiplier per role (e.g. 1.5× after 8 h, 2× after 12 h) |
| Create / Edit Rule | CRUD on overtime rules per role with `*` wildcard for all roles |
| Toggle Active | Enable or disable a rule without deleting it |
| Payroll Integration | OT multipliers are applied in pay summary and payroll export |

#### Leave Management
| Feature | Description |
|---|---|
| All Leave Records | View and filter leave across all staff |
| Approve / Reject | One-click quick-action on pending requests |
| Create / Edit Leave | Admin-initiated leave record (e.g. record a no-show) |
| Delete Leave | Remove incorrect records |
| Entitlement Editor | Set annual and sick day allowance per employee per year |
| Balance Overview | Leave balance table for all active staff at once |
| Leave Calendar | Month grid showing every employee's approved leave at a glance |

#### Location Management
| Feature | Description |
|---|---|
| Locations List | All configured clock-in venues |
| Create / Edit Location | Add or rename a location |
| Location Image | Upload a photo for the location card |
| Delete Location | Soft-delete a location |

#### QR Code Management
| Feature | Description |
|---|---|
| QR Gallery | All codes with expiry and revocation status |
| Generate QR | New code for a location with configurable expiry (1–72 hours) |
| Multi-Use Codes | Same QR can be scanned by multiple staff until revoked |
| Revoke Code | Immediately disable a QR code |
| Delete Code | Permanently remove revoked / expired records |

#### Reports & Payroll
| Feature | Description |
|---|---|
| By Employee | Shifts, total hours, avg hours/shift, gross pay per staff member |
| By Location | Shifts, hours, labour cost per venue |
| By Role | Head count, shifts, hours, labour cost per job role |
| By Date | Daily breakdown of shifts, hours, labour cost |
| Shift Detail | Full row-level log with clock times, OT flag, and cost per shift |
| Report Filters | Date presets (today / yesterday / week / month / last month) + custom range |
| Multi-Select Filters | Filter by specific staff, locations, or roles simultaneously |
| CSV Export | Download any report view as a CSV |
| Payroll Export | Monthly payroll CSV (regular hours, OT hours, tips, gross pay, leave days) |

#### Audit Log
| Feature | Description |
|---|---|
| Activity Log | Full chronological log of who changed what and when (shifts, leave, staff, OT rules) |
| Filter | Filter by entity type, action (create/update/delete), admin name, or date range |
| Paginated | 50 records per page with prev/next navigation |

#### Roles & Access Control
| Feature | Description |
|---|---|
| Role List | View system and custom roles |
| Create Role | Add a custom job role |
| Delete Role | Remove custom roles (system roles admin / manager / employee are protected) |

#### Account Settings
| Feature | Description |
|---|---|
| Own Profile | View and update personal info |
| Change Password | Self-service password update |

#### Notification Centre
| Feature | Description |
|---|---|
| Bell Icon | Unread notification count badge in the top bar |
| Notification Drawer | List of leave requests, missed clock-outs, and system alerts with mark-read actions |

---

### Backend API

| Controller | Key Endpoints |
|---|---|
| `AuthController` | Login (portal-aware), refresh token, change/forgot/force-change password, profile, magic-link generate + redeem |
| `ClockController` | Clock in (QR-validated + roster-enforced), clock out, active shift, shift history, pay summary |
| `RosterController` | Staff's own schedule, confirm shift; admin CRUD + bulk CSV import + template apply |
| `LeaveController` | Staff apply / cancel / view leave + cert upload; balance query |
| `StaffController` | CRUD staff, activate/deactivate, reset password, document upload, role CRUD, performance stats |
| `AdminController` | Dashboard stats (incl. open-shifts count), shift view/edit, leave admin, payroll export, workforce report, overtime rules, shift templates, audit log, tips pooling |
| `LocationsController` | Location CRUD + image upload |
| `QrController` | QR generate / revoke / delete |
| `NotificationController` | List notifications, mark read / read-all |
| `AnnouncementController` | Staff feed + mark-read; admin compose / manage / read-receipt count |
| `ShiftSwapController` | Staff request / respond / cancel; admin review (approve/reject) |
| `PushController` | VAPID public key, subscribe, unsubscribe |

**Background Jobs (Hangfire)**
| Job | Schedule | Description |
|---|---|---|
| `CheckMissedClockOuts` | Every 30 min | Flags shifts open beyond configurable hours; creates admin notification |

---

## Portal Access Rules

| Portal | Who can log in | Enforcement |
|---|---|---|
| **Staff App** | Employees and managers only | Admin accounts blocked at API (HTTP 401) |
| **Admin App** | Managers and admins only | Plain-staff accounts blocked at API (HTTP 401) |
| **Clock-In** | Any non-admin staff | Must have a rostered entry for today at that location; admins exempt for oversight |

---

## Running Locally

### Prerequisites
- .NET 8 SDK
- Node.js 20+
- PostgreSQL 15+
- Angular CLI (`npm install -g @angular/cli`)

### Backend
```bash
cd backend
cp .env.example .env          # fill in DB connection string + JWT secret
dotnet ef database update     # apply EF migrations
dotnet run --project src/Api
# API available at https://localhost:5001
```

**Optional — Push Notifications (VAPID)**  
Generate a VAPID key pair (`npx web-push generate-vapid-keys`) and add to `appsettings.json`:
```json
"WebPush": {
  "Subject": "mailto:admin@yourdomain.com",
  "PublicKey": "<your-public-key>",
  "PrivateKey": "<your-private-key>"
}
```
Push notifications are silently skipped if these keys are absent.

### Frontend
```bash
npm install                    # install Angular workspace deps
ng build shared-lib            # must build library first
ng serve staff-app             # http://localhost:4200  (staff portal)
ng serve admin-app             # http://localhost:4300  (admin portal)
```

### Docker (full stack)
```bash
docker compose -f docker-compose.local.yml up --build
```

---

## Building

```bash
# Library (must build first)
ng build shared-lib

# Applications
ng build staff-app
ng build admin-app

# Backend
dotnet publish backend/src/Api -c Release
```
