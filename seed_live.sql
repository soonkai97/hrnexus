-- ============================================================
--  Live / active data seed  (idempotent)
--  Adds:
--    1. Active shifts (EndTime = NULL) → "staff currently working"
--    2. Partial-day leave requests     → "few hours leave"
--    3. Work logs for Sam (admin)      → history shows data
-- ============================================================
DO $$
DECLARE
  v_marcus UUID; v_alex UUID; v_sam  UUID;
  v_jay    UUID; v_nina UUID; v_kai  UUID; v_tasha UUID; v_leo UUID;
  v_mainbar UUID; v_vip UUID; v_stage UUID; v_entrance UUID;
BEGIN

  SELECT "Id" INTO v_marcus FROM "Staff" WHERE "Email"='staff@venue.com'       LIMIT 1;
  SELECT "Id" INTO v_alex   FROM "Staff" WHERE "Email"='manager@venue.com'     LIMIT 1;
  SELECT "Id" INTO v_sam    FROM "Staff" WHERE "Email"='admin@venue.com'       LIMIT 1;
  SELECT "Id" INTO v_jay    FROM "Staff" WHERE "Email"='jay.park@venue.com'    LIMIT 1;
  SELECT "Id" INTO v_nina   FROM "Staff" WHERE "Email"='nina.james@venue.com'  LIMIT 1;
  SELECT "Id" INTO v_kai    FROM "Staff" WHERE "Email"='kai.wong@venue.com'    LIMIT 1;
  SELECT "Id" INTO v_tasha  FROM "Staff" WHERE "Email"='tasha.li@venue.com'    LIMIT 1;
  SELECT "Id" INTO v_leo    FROM "Staff" WHERE "Email"='leo.rahman@venue.com'  LIMIT 1;

  SELECT "Id" INTO v_mainbar  FROM "Locations" WHERE "Name"='Main Bar'   LIMIT 1;
  SELECT "Id" INTO v_vip      FROM "Locations" WHERE "Name"='VIP Lounge' LIMIT 1;
  SELECT "Id" INTO v_stage    FROM "Locations" WHERE "Name"='Main Stage' LIMIT 1;
  SELECT "Id" INTO v_entrance FROM "Locations" WHERE "Name"='Entrance'   LIMIT 1;

  -- ── 1. ACTIVE SHIFTS (currently working, EndTime = NULL) ──────────────────
  -- Marcus: started at 20:00 tonight, still clocked in
  INSERT INTO "WorkLogs"("Id","EmployeeId","RoleName","LocationId","LocationName","WorkDate","StartTime","EndTime","IsManualEntry","Notes","HourlyRateSnapshot","CreatedAt")
  SELECT gen_random_uuid(), v_marcus, 'bartender', v_mainbar, 'Main Bar',
         CURRENT_DATE, '20:00', NULL, FALSE, NULL, 18.00, NOW()
  WHERE NOT EXISTS(SELECT 1 FROM "WorkLogs" WHERE "EmployeeId"=v_marcus AND "WorkDate"=CURRENT_DATE AND "EndTime" IS NULL);

  -- Jay: bouncer, started at 20:30
  INSERT INTO "WorkLogs"("Id","EmployeeId","RoleName","LocationId","LocationName","WorkDate","StartTime","EndTime","IsManualEntry","Notes","HourlyRateSnapshot","CreatedAt")
  SELECT gen_random_uuid(), v_jay, 'bouncer', v_entrance, 'Entrance',
         CURRENT_DATE, '20:30', NULL, FALSE, NULL, 16.00, NOW()
  WHERE NOT EXISTS(SELECT 1 FROM "WorkLogs" WHERE "EmployeeId"=v_jay AND "WorkDate"=CURRENT_DATE AND "EndTime" IS NULL);

  -- Nina: hostess, VIP Lounge, started at 21:00
  INSERT INTO "WorkLogs"("Id","EmployeeId","RoleName","LocationId","LocationName","WorkDate","StartTime","EndTime","IsManualEntry","Notes","HourlyRateSnapshot","CreatedAt")
  SELECT gen_random_uuid(), v_nina, 'hostess', v_vip, 'VIP Lounge',
         CURRENT_DATE, '21:00', NULL, FALSE, NULL, 17.00, NOW()
  WHERE NOT EXISTS(SELECT 1 FROM "WorkLogs" WHERE "EmployeeId"=v_nina AND "WorkDate"=CURRENT_DATE AND "EndTime" IS NULL);

  -- ── 2. PARTIAL-DAY LEAVE REQUESTS (few hours) ─────────────────────────────
  -- Kai: 2-hour medical appointment tomorrow morning
  INSERT INTO "LeaveRequests"("Id","EmployeeId","LeaveType","StartDate","EndDate","StartTime","EndTime","Notes","Status","MedicalCertUrl","CreatedAt")
  SELECT gen_random_uuid(), v_kai, 'medical',
         (CURRENT_DATE + 1)::TIMESTAMPTZ, (CURRENT_DATE + 1)::TIMESTAMPTZ,
         '09:00', '11:00',
         'Doctor appointment – ENT specialist', 'pending', NULL, NOW()
  WHERE NOT EXISTS(SELECT 1 FROM "LeaveRequests" WHERE "EmployeeId"=v_kai AND "StartTime"='09:00' AND "StartDate"::DATE = CURRENT_DATE + 1);

  -- Tasha: 3-hour personal errand this Friday afternoon
  INSERT INTO "LeaveRequests"("Id","EmployeeId","LeaveType","StartDate","EndDate","StartTime","EndTime","Notes","Status","MedicalCertUrl","CreatedAt")
  SELECT gen_random_uuid(), v_tasha, 'annual',
         (DATE_TRUNC('week', CURRENT_DATE) + INTERVAL '4 days')::TIMESTAMPTZ,
         (DATE_TRUNC('week', CURRENT_DATE) + INTERVAL '4 days')::TIMESTAMPTZ,
         '14:00', '17:00',
         'Bank appointment – need to leave early Friday', 'pending', NULL, NOW()
  WHERE NOT EXISTS(SELECT 1 FROM "LeaveRequests" WHERE "EmployeeId"=v_tasha AND "StartTime"='14:00');

  -- Leo: half-day off next Monday (leave early)
  INSERT INTO "LeaveRequests"("Id","EmployeeId","LeaveType","StartDate","EndDate","StartTime","EndTime","Notes","Status","MedicalCertUrl","CreatedAt")
  SELECT gen_random_uuid(), v_leo, 'annual',
         (DATE_TRUNC('week', CURRENT_DATE) + INTERVAL '7 days')::TIMESTAMPTZ,
         (DATE_TRUNC('week', CURRENT_DATE) + INTERVAL '7 days')::TIMESTAMPTZ,
         '08:00', '13:00',
         'Morning off for family matter', 'pending', NULL, NOW()
  WHERE NOT EXISTS(SELECT 1 FROM "LeaveRequests" WHERE "EmployeeId"=v_leo AND "StartTime"='08:00');

  -- Jay: 2-hour late start this week (approved)
  INSERT INTO "LeaveRequests"("Id","EmployeeId","LeaveType","StartDate","EndDate","StartTime","EndTime","Notes","Status","MedicalCertUrl","CreatedAt")
  SELECT gen_random_uuid(), v_jay, 'annual',
         CURRENT_DATE::TIMESTAMPTZ, CURRENT_DATE::TIMESTAMPTZ,
         '10:00', '12:00',
         'Late start – personal appointment', 'approved', NULL, NOW()
  WHERE NOT EXISTS(SELECT 1 FROM "LeaveRequests" WHERE "EmployeeId"=v_jay AND "StartTime"='10:00' AND "StartDate"::DATE = CURRENT_DATE);

  -- Marcus: 4-hour MC on a recent sick day (approved)
  INSERT INTO "LeaveRequests"("Id","EmployeeId","LeaveType","StartDate","EndDate","StartTime","EndTime","Notes","Status","MedicalCertUrl","CreatedAt")
  SELECT gen_random_uuid(), v_marcus, 'sick',
         (CURRENT_DATE - 5)::TIMESTAMPTZ, (CURRENT_DATE - 5)::TIMESTAMPTZ,
         '08:00', '12:00',
         'Clinic visit – fever', 'approved', NULL, NOW()
  WHERE NOT EXISTS(SELECT 1 FROM "LeaveRequests" WHERE "EmployeeId"=v_marcus AND "StartTime"='08:00' AND "StartDate"::DATE = CURRENT_DATE - 5);

  -- ── 3. WORK LOGS FOR SAM (admin) ─────────────────────────────────────────
  -- Sam works admin shifts (reviewing payroll, managing staff) – manual entries
  INSERT INTO "WorkLogs"("Id","EmployeeId","RoleName","LocationId","LocationName","WorkDate","StartTime","EndTime","IsManualEntry","Notes","HourlyRateSnapshot","CreatedAt")
  SELECT gen_random_uuid(), v_sam, 'manager',
         COALESCE(v_mainbar, gen_random_uuid()::UUID), 'Main Bar',
         d::DATE, '10:00', '18:00', TRUE,
         CASE WHEN EXTRACT(DOW FROM d) = 1 THEN 'Payroll review Monday'
              WHEN EXTRACT(DOW FROM d) = 3 THEN 'Staff scheduling'
              ELSE NULL END,
         25.00, d + INTERVAL '10 hours'
  FROM generate_series(CURRENT_DATE-60, CURRENT_DATE-1, '1 day'::INTERVAL) d
  WHERE EXTRACT(DOW FROM d) IN (1,2,3,4,5)   -- Mon-Fri
    AND NOT EXISTS(SELECT 1 FROM "WorkLogs" WHERE "EmployeeId"=v_sam AND "WorkDate"=d::DATE);

END $$;
