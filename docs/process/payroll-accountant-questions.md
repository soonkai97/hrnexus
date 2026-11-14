# Payroll: questions for the accountant

> **Purpose:** HRNexus is about to build its monthly payroll calculator (Phase 8b). The calculator is written; the **rates and rounding rules it uses must be confirmed by you** before any real payroll is approved. Nothing here is filed or paid automatically: staff are paid and statutory amounts are submitted by a person.
> **Company facts the system assumes:** Peninsular Malaysia, Malaysian citizens and permanent residents only, monthly salary, 10 or more Malaysian employees, about 20 to a few hundred staff.
> **How to answer:** write the answer under each question, or correct the "what we assumed" line. A one-line answer is enough.

---

## 1. EPF, SOCSO and EIS: official band amounts or a percentage?

Our current rows are simple percentages of the wage (placeholders). We believe the official schedules are **band tables** and the percentage gives the wrong sen for many wages.

- **What we assumed:** EPF employee 11%; employer 13% (12% above RM5,000), rounded **up to the next ringgit**. SOCSO category 1: employee 0.5%, employer 1.75%. EIS: 0.2% each. SOCSO and EIS wage ceiling RM6,000 (from 1 Oct 2024); amounts rounded to the nearest 5 sen.
- **Please tell us:** are the contributions taken from the official tables (EPF Third Schedule, PERKESO tables)? If so, please send the current tables (a spreadsheet or the official PDF is fine). If a percentage is acceptable, what is the exact rounding rule for each scheme?
- **Also:** what changes for employees aged 60 or over (EPF, SOCSO, EIS)? Until we have this, anyone over 60 is flagged and cannot be approved.

## 2. EPF employer rate

- **What we assumed:** if the wage is RM5,000 or less the employer rate is 13%, above RM5,000 it is 12%, and the chosen rate applies to the **whole** wage (not 13% on the first RM5,000 and 12% on the rest).
- **Please tell us:** is that right?

## 3. Which earnings count for EPF, SOCSO and EIS

- **What we assumed:** basic salary, allowances, overtime and other earnings all count, after the unpaid-leave deduction.
- **Please tell us:** for each of EPF and (SOCSO and EIS), which of these are **excluded**: overtime, allowances (which kinds?), bonuses, commissions, one-off payments, back pay.

## 4. Deductions from wages (Employment Act, section 24)

- **What we assumed:** the total of "other deductions" (cash-advance repayments and agreed deductions) may not exceed **50% of wages**, they are applied last, never take net pay below zero, and anything not applied carries to the next month. Statutory contributions and tax are not counted in that 50%.
- **Please tell us:** is 50% correct and of which wages? Which deductions are allowed (advances, agreed repayments; are fines allowed at all)?

## 5. Unpaid leave and part-month pay

- **What we assumed:** unpaid-leave deduction = monthly basic ÷ **26** × unpaid working days (never more than the month's basic). A person who joins or leaves mid-month is paid by **calendar days** in the month.
- **Please tell us:** is the divisor 26 right for your company (some use calendar days or actual working days), and is calendar-day proration acceptable?

## 6. Monthly tax deduction (PCB) and year-end

- **Decision already made:** the system does **not** calculate PCB yet. You or the payroll preparer will type the PCB amount per person each month (with a reason). Please confirm how you will supply it.
- **For later:** the year-end statement (EA form) is not built yet; tell us if you need a particular layout.

## 7. Before the first real payroll

We recommend a **3-month parallel run** (the system and your manual calculation side by side for about 20 sample staff) before anyone is paid from the system, and a sign-off that the rates above are verified. Can you support that?

---

### After you answer

Your answers change **data** (the rate tables) and a few test examples, not the program. Each worked example will be sent back to you as a short table (inputs, formula and result for each line) to sign off or correct.
