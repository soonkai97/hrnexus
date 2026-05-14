-- ============================================================
--  NightCrew Demo Seed  (idempotent – safe to run multiple times)
-- ============================================================

DO $$
DECLARE
  -- Staff IDs (resolved from seeded emails)
  v_marcus   UUID;
  v_alex     UUID;
  v_sam      UUID;

  -- Extra staff IDs
  v_jay      UUID;
  v_nina     UUID;
  v_kai      UUID;
  v_tasha    UUID;
  v_leo      UUID;

  -- Location IDs
  v_mainbar  UUID;
  v_vip      UUID;
  v_stage    UUID;
  v_entrance UUID;

BEGIN

  -- ── 1. Resolve existing staff ───────────────────────────────────────────────
  SELECT "Id" INTO v_marcus FROM "Staff" WHERE "Email" = 'staff@venue.com'   LIMIT 1;
  SELECT "Id" INTO v_alex   FROM "Staff" WHERE "Email" = 'manager@venue.com' LIMIT 1;
  SELECT "Id" INTO v_sam    FROM "Staff" WHERE "Email" = 'admin@venue.com'   LIMIT 1;

  -- ── 2. Resolve locations ────────────────────────────────────────────────────
  SELECT "Id" INTO v_mainbar  FROM "Locations" WHERE "Name" = 'Main Bar'    LIMIT 1;
  SELECT "Id" INTO v_vip      FROM "Locations" WHERE "Name" = 'VIP Lounge'  LIMIT 1;
  SELECT "Id" INTO v_stage    FROM "Locations" WHERE "Name" = 'Main Stage'  LIMIT 1;
  SELECT "Id" INTO v_entrance FROM "Locations" WHERE "Name" = 'Entrance'    LIMIT 1;

  -- ── 3. Set hourly rates on roles ────────────────────────────────────────────
  UPDATE "Roles" SET "HourlyRate" = 18.00 WHERE "Name" = 'bartender';
  UPDATE "Roles" SET "HourlyRate" = 15.00 WHERE "Name" = 'waitstaff';
  UPDATE "Roles" SET "HourlyRate" = 20.00 WHERE "Name" = 'bouncer';
  UPDATE "Roles" SET "HourlyRate" = 25.00 WHERE "Name" = 'dj';
  UPDATE "Roles" SET "HourlyRate" = 22.00 WHERE "Name" = 'manager';

  -- ── 4. Add extra staff members ──────────────────────────────────────────────
  -- Jay (bouncer)
  IF NOT EXISTS (SELECT 1 FROM "Staff" WHERE "Email" = 'jay.park@venue.com') THEN
    v_jay := gen_random_uuid();
    INSERT INTO "Staff" ("Id","StaffId","FirstName","LastName","Email","PasswordHash",
      "IsActive","CreatedAt","PhoneNumber","IcNo","PassportNo","DateOfBirth","PreferredLanguage","MustChangePassword")
    VALUES (v_jay,'NC1004','Jay','Park','jay.park@venue.com',
      '$2a$11$92IXUNpkjO0rOQ5byMi.Ye4oKoEa3Ro9llC/.og/at2.uheWG/igi', -- password123
      TRUE, NOW()-INTERVAL '60 days','+60 11-4567 8901','950612-05-5005',NULL,'1995-06-12','en',FALSE);
    INSERT INTO "StaffRoles" ("EmployeeId","Role") VALUES (v_jay,'bouncer');
  ELSE
    SELECT "Id" INTO v_jay FROM "Staff" WHERE "Email" = 'jay.park@venue.com';
  END IF;

  -- Nina (waitstaff)
  IF NOT EXISTS (SELECT 1 FROM "Staff" WHERE "Email" = 'nina.james@venue.com') THEN
    v_nina := gen_random_uuid();
    INSERT INTO "Staff" ("Id","StaffId","FirstName","LastName","Email","PasswordHash",
      "IsActive","CreatedAt","PhoneNumber","IcNo","PassportNo","DateOfBirth","PreferredLanguage","MustChangePassword")
    VALUES (v_nina,'NC1005','Nina','James','nina.james@venue.com',
      '$2a$11$92IXUNpkjO0rOQ5byMi.Ye4oKoEa3Ro9llC/.og/at2.uheWG/igi',
      TRUE, NOW()-INTERVAL '45 days','+60 11-5678 9012','970822-06-6006',NULL,'1997-08-22','en',FALSE);
    INSERT INTO "StaffRoles" ("EmployeeId","Role") VALUES (v_nina,'waitstaff');
  ELSE
    SELECT "Id" INTO v_nina FROM "Staff" WHERE "Email" = 'nina.james@venue.com';
  END IF;

  -- Kai (DJ)
  IF NOT EXISTS (SELECT 1 FROM "Staff" WHERE "Email" = 'kai.wong@venue.com') THEN
    v_kai := gen_random_uuid();
    INSERT INTO "Staff" ("Id","StaffId","FirstName","LastName","Email","PasswordHash",
      "IsActive","CreatedAt","PhoneNumber","IcNo","PassportNo","DateOfBirth","PreferredLanguage","MustChangePassword")
    VALUES (v_kai,'NC1006','Kai','Wong','kai.wong@venue.com',
      '$2a$11$92IXUNpkjO0rOQ5byMi.Ye4oKoEa3Ro9llC/.og/at2.uheWG/igi',
      TRUE, NOW()-INTERVAL '30 days','+60 11-6789 0123','920415-07-7007',NULL,'1992-04-15','en',FALSE);
    INSERT INTO "StaffRoles" ("EmployeeId","Role") VALUES (v_kai,'dj');
  ELSE
    SELECT "Id" INTO v_kai FROM "Staff" WHERE "Email" = 'kai.wong@venue.com';
  END IF;

  -- Tasha (bartender + waitstaff)
  IF NOT EXISTS (SELECT 1 FROM "Staff" WHERE "Email" = 'tasha.li@venue.com') THEN
    v_tasha := gen_random_uuid();
    INSERT INTO "Staff" ("Id","StaffId","FirstName","LastName","Email","PasswordHash",
      "IsActive","CreatedAt","PhoneNumber","IcNo","PassportNo","DateOfBirth","PreferredLanguage","MustChangePassword")
    VALUES (v_tasha,'NC1007','Tasha','Li','tasha.li@venue.com',
      '$2a$11$92IXUNpkjO0rOQ5byMi.Ye4oKoEa3Ro9llC/.og/at2.uheWG/igi',
      TRUE, NOW()-INTERVAL '20 days','+60 11-7890 1234','991130-08-8008',NULL,'1999-11-30','zh',FALSE);
    INSERT INTO "StaffRoles" ("EmployeeId","Role") VALUES (v_tasha,'bartender'),(v_tasha,'waitstaff');
  ELSE
    SELECT "Id" INTO v_tasha FROM "Staff" WHERE "Email" = 'tasha.li@venue.com';
  END IF;

  -- Leo (bouncer)
  IF NOT EXISTS (SELECT 1 FROM "Staff" WHERE "Email" = 'leo.rahman@venue.com') THEN
    v_leo := gen_random_uuid();
    INSERT INTO "Staff" ("Id","StaffId","FirstName","LastName","Email","PasswordHash",
      "IsActive","CreatedAt","PhoneNumber","IcNo","PassportNo","DateOfBirth","PreferredLanguage","MustChangePassword")
    VALUES (v_leo,'NC1008','Leo','Rahman','leo.rahman@venue.com',
      '$2a$11$92IXUNpkjO0rOQ5byMi.Ye4oKoEa3Ro9llC/.og/at2.uheWG/igi',
      TRUE, NOW()-INTERVAL '10 days','+60 11-8901 2345','930708-09-9009',NULL,'1993-07-08','en',FALSE);
    INSERT INTO "StaffRoles" ("EmployeeId","Role") VALUES (v_leo,'bouncer');
  ELSE
    SELECT "Id" INTO v_leo FROM "Staff" WHERE "Email" = 'leo.rahman@venue.com';
  END IF;

  -- ── 5. Overtime rules ───────────────────────────────────────────────────────
  IF NOT EXISTS (SELECT 1 FROM "OvertimeRules" WHERE "Role" = '*') THEN
    INSERT INTO "OvertimeRules" ("Id","Role","ThresholdHours","Multiplier","IsActive","CreatedAt")
    VALUES (gen_random_uuid(),'*',8.0,1.5,TRUE,NOW());
  END IF;
  IF NOT EXISTS (SELECT 1 FROM "OvertimeRules" WHERE "Role" = 'bouncer') THEN
    INSERT INTO "OvertimeRules" ("Id","Role","ThresholdHours","Multiplier","IsActive","CreatedAt")
    VALUES (gen_random_uuid(),'bouncer',6.0,1.75,TRUE,NOW());
  END IF;
  IF NOT EXISTS (SELECT 1 FROM "OvertimeRules" WHERE "Role" = 'dj') THEN
    INSERT INTO "OvertimeRules" ("Id","Role","ThresholdHours","Multiplier","IsActive","CreatedAt")
    VALUES (gen_random_uuid(),'dj',5.0,2.0,TRUE,NOW());
  END IF;

  -- ── 6. Announcements ────────────────────────────────────────────────────────
  IF NOT EXISTS (SELECT 1 FROM "Announcements" WHERE "Title" = 'Staff Meeting — May 20') THEN
    INSERT INTO "Announcements" ("Id","Title","Body","TargetRoles","IsActive","CreatedById","CreatedByName","CreatedAt","ExpiresAt")
    VALUES (gen_random_uuid(),'Staff Meeting — May 20',
      'Mandatory all-hands meeting this Tuesday at 6 PM in the VIP Lounge. Please confirm attendance with your manager.',
      '*',TRUE,v_sam,'Sam Chen',NOW()-INTERVAL '3 days',NOW()+INTERVAL '7 days');
  END IF;

  IF NOT EXISTS (SELECT 1 FROM "Announcements" WHERE "Title" = 'New Dress Code Policy') THEN
    INSERT INTO "Announcements" ("Id","Title","Body","TargetRoles","IsActive","CreatedById","CreatedByName","CreatedAt","ExpiresAt")
    VALUES (gen_random_uuid(),'New Dress Code Policy',
      'Effective immediately: all bartenders must wear the new black-and-gold uniform. Shirts available at the stockroom.',
      'bartender',TRUE,v_alex,'Alex Rivera',NOW()-INTERVAL '7 days',NULL);
  END IF;

  IF NOT EXISTS (SELECT 1 FROM "Announcements" WHERE "Title" = 'Bonus Night — Saturday') THEN
    INSERT INTO "Announcements" ("Id","Title","Body","TargetRoles","IsActive","CreatedById","CreatedByName","CreatedAt","ExpiresAt")
    VALUES (gen_random_uuid(),'Bonus Night — Saturday',
      'This Saturday is a VIP event. All staff working will receive a 20% bonus on their shift pay. Double security required at the entrance.',
      '*',TRUE,v_sam,'Sam Chen',NOW()-INTERVAL '1 day',NOW()+INTERVAL '5 days');
  END IF;

  IF NOT EXISTS (SELECT 1 FROM "Announcements" WHERE "Title" = 'Ramadan Hours Update') THEN
    INSERT INTO "Announcements" ("Id","Title","Body","TargetRoles","IsActive","CreatedById","CreatedByName","CreatedAt","ExpiresAt")
    VALUES (gen_random_uuid(),'Ramadan Hours Update',
      'Bar closes 30 minutes earlier during Ramadan. New closing time is 2:30 AM. Please adjust shift scheduling accordingly.',
      'bartender,manager',FALSE,v_alex,'Alex Rivera',NOW()-INTERVAL '30 days',NOW()-INTERVAL '2 days');
  END IF;

  -- ── 7. Work logs (past 4 weeks) ─────────────────────────────────────────────

  -- Marcus – bartender, Main Bar
  INSERT INTO "WorkLogs" ("Id","EmployeeId","RoleName","LocationId","LocationName","WorkDate","StartTime","EndTime","IsManualEntry","Notes","HourlyRateSnapshot","CreatedAt")
  SELECT gen_random_uuid(), v_marcus,'bartender',v_mainbar,'Main Bar',
    (CURRENT_DATE - (n || ' days')::INTERVAL)::DATE,
    '20:00', CASE WHEN n % 3 = 0 THEN '04:30' ELSE '03:00' END,
    FALSE, NULL, 18.00, NOW() - (n || ' days')::INTERVAL
  FROM generate_series(2,28,3) AS n
  WHERE NOT EXISTS (
    SELECT 1 FROM "WorkLogs" WHERE "EmployeeId" = v_marcus
      AND "WorkDate" = (CURRENT_DATE - (n || ' days')::INTERVAL)::DATE
  );

  -- Alex – manager, VIP Lounge
  INSERT INTO "WorkLogs" ("Id","EmployeeId","RoleName","LocationId","LocationName","WorkDate","StartTime","EndTime","IsManualEntry","Notes","HourlyRateSnapshot","CreatedAt")
  SELECT gen_random_uuid(), v_alex,'manager',v_vip,'VIP Lounge',
    (CURRENT_DATE - (n || ' days')::INTERVAL)::DATE,
    '19:00','02:00',FALSE,NULL,22.00, NOW() - (n || ' days')::INTERVAL
  FROM generate_series(1,27,3) AS n
  WHERE NOT EXISTS (
    SELECT 1 FROM "WorkLogs" WHERE "EmployeeId" = v_alex
      AND "WorkDate" = (CURRENT_DATE - (n || ' days')::INTERVAL)::DATE
  );

  -- Jay – bouncer, Entrance
  INSERT INTO "WorkLogs" ("Id","EmployeeId","RoleName","LocationId","LocationName","WorkDate","StartTime","EndTime","IsManualEntry","Notes","HourlyRateSnapshot","CreatedAt")
  SELECT gen_random_uuid(), v_jay,'bouncer',v_entrance,'Entrance',
    (CURRENT_DATE - (n || ' days')::INTERVAL)::DATE,
    '21:00','05:00',FALSE,NULL,20.00, NOW() - (n || ' days')::INTERVAL
  FROM generate_series(1,26,2) AS n
  WHERE NOT EXISTS (
    SELECT 1 FROM "WorkLogs" WHERE "EmployeeId" = v_jay
      AND "WorkDate" = (CURRENT_DATE - (n || ' days')::INTERVAL)::DATE
  );

  -- Nina – waitstaff, VIP Lounge
  INSERT INTO "WorkLogs" ("Id","EmployeeId","RoleName","LocationId","LocationName","WorkDate","StartTime","EndTime","IsManualEntry","Notes","HourlyRateSnapshot","CreatedAt")
  SELECT gen_random_uuid(), v_nina,'waitstaff',v_vip,'VIP Lounge',
    (CURRENT_DATE - (n || ' days')::INTERVAL)::DATE,
    '20:00','03:30',TRUE,'Manual entry',15.00, NOW() - (n || ' days')::INTERVAL
  FROM generate_series(2,25,3) AS n
  WHERE NOT EXISTS (
    SELECT 1 FROM "WorkLogs" WHERE "EmployeeId" = v_nina
      AND "WorkDate" = (CURRENT_DATE - (n || ' days')::INTERVAL)::DATE
  );

  -- Kai – dj, Main Stage
  INSERT INTO "WorkLogs" ("Id","EmployeeId","RoleName","LocationId","LocationName","WorkDate","StartTime","EndTime","IsManualEntry","Notes","HourlyRateSnapshot","CreatedAt")
  SELECT gen_random_uuid(), v_kai,'dj',v_stage,'Main Stage',
    (CURRENT_DATE - (n || ' days')::INTERVAL)::DATE,
    '22:00','04:00',FALSE,'Set 1 & 2',25.00, NOW() - (n || ' days')::INTERVAL
  FROM generate_series(1,21,7) AS n
  WHERE NOT EXISTS (
    SELECT 1 FROM "WorkLogs" WHERE "EmployeeId" = v_kai
      AND "WorkDate" = (CURRENT_DATE - (n || ' days')::INTERVAL)::DATE
  );

  -- Tasha – bartender, Main Bar
  INSERT INTO "WorkLogs" ("Id","EmployeeId","RoleName","LocationId","LocationName","WorkDate","StartTime","EndTime","IsManualEntry","Notes","HourlyRateSnapshot","CreatedAt")
  SELECT gen_random_uuid(), v_tasha,'bartender',v_mainbar,'Main Bar',
    (CURRENT_DATE - (n || ' days')::INTERVAL)::DATE,
    '19:30','02:30',FALSE,NULL,18.00, NOW() - (n || ' days')::INTERVAL
  FROM generate_series(3,24,3) AS n
  WHERE NOT EXISTS (
    SELECT 1 FROM "WorkLogs" WHERE "EmployeeId" = v_tasha
      AND "WorkDate" = (CURRENT_DATE - (n || ' days')::INTERVAL)::DATE
  );

  -- Leo – bouncer, Entrance (with OT note)
  INSERT INTO "WorkLogs" ("Id","EmployeeId","RoleName","LocationId","LocationName","WorkDate","StartTime","EndTime","IsManualEntry","Notes","HourlyRateSnapshot","CreatedAt")
  SELECT gen_random_uuid(), v_leo,'bouncer',v_entrance,'Entrance',
    (CURRENT_DATE - (n || ' days')::INTERVAL)::DATE,
    '20:00','05:30',FALSE,'Extended shift – VIP event',20.00, NOW() - (n || ' days')::INTERVAL
  FROM generate_series(2,20,4) AS n
  WHERE NOT EXISTS (
    SELECT 1 FROM "WorkLogs" WHERE "EmployeeId" = v_leo
      AND "WorkDate" = (CURRENT_DATE - (n || ' days')::INTERVAL)::DATE
  );

  -- ── 8. Leave requests ───────────────────────────────────────────────────────

  -- Marcus – approved annual leave last week
  IF NOT EXISTS (SELECT 1 FROM "LeaveRequests" WHERE "EmployeeId" = v_marcus AND "LeaveType" = 'annual' AND "Status" = 'approved') THEN
    INSERT INTO "LeaveRequests" ("Id","EmployeeId","LeaveType","StartDate","EndDate","StartTime","EndTime","Notes","Status","MedicalCertUrl","CreatedAt")
    VALUES (gen_random_uuid(),v_marcus,'annual',
      NOW()-INTERVAL '12 days', NOW()-INTERVAL '10 days',NULL,NULL,'Family vacation','approved',NULL,NOW()-INTERVAL '15 days');
  END IF;

  -- Jay – pending sick leave
  IF NOT EXISTS (SELECT 1 FROM "LeaveRequests" WHERE "EmployeeId" = v_jay AND "LeaveType" = 'sick') THEN
    INSERT INTO "LeaveRequests" ("Id","EmployeeId","LeaveType","StartDate","EndDate","StartTime","EndTime","Notes","Status","MedicalCertUrl","CreatedAt")
    VALUES (gen_random_uuid(),v_jay,'sick',
      NOW()-INTERVAL '2 days', NOW()-INTERVAL '1 day',NULL,NULL,'Fever and flu','pending',NULL,NOW()-INTERVAL '2 days');
  END IF;

  -- Nina – approved annual
  IF NOT EXISTS (SELECT 1 FROM "LeaveRequests" WHERE "EmployeeId" = v_nina AND "LeaveType" = 'annual') THEN
    INSERT INTO "LeaveRequests" ("Id","EmployeeId","LeaveType","StartDate","EndDate","StartTime","EndTime","Notes","Status","MedicalCertUrl","CreatedAt")
    VALUES (gen_random_uuid(),v_nina,'annual',
      NOW()+INTERVAL '3 days', NOW()+INTERVAL '5 days',NULL,NULL,'Birthday trip','approved',NULL,NOW()-INTERVAL '5 days');
  END IF;

  -- Kai – rejected unpaid
  IF NOT EXISTS (SELECT 1 FROM "LeaveRequests" WHERE "EmployeeId" = v_kai AND "LeaveType" = 'unpaid') THEN
    INSERT INTO "LeaveRequests" ("Id","EmployeeId","LeaveType","StartDate","EndDate","StartTime","EndTime","Notes","Status","MedicalCertUrl","CreatedAt")
    VALUES (gen_random_uuid(),v_kai,'unpaid',
      NOW()-INTERVAL '20 days', NOW()-INTERVAL '18 days',NULL,NULL,'Personal matter','rejected',NULL,NOW()-INTERVAL '22 days');
  END IF;

  -- Tasha – pending annual (future)
  IF NOT EXISTS (SELECT 1 FROM "LeaveRequests" WHERE "EmployeeId" = v_tasha AND "LeaveType" = 'annual') THEN
    INSERT INTO "LeaveRequests" ("Id","EmployeeId","LeaveType","StartDate","EndDate","StartTime","EndTime","Notes","Status","MedicalCertUrl","CreatedAt")
    VALUES (gen_random_uuid(),v_tasha,'annual',
      NOW()+INTERVAL '7 days', NOW()+INTERVAL '9 days',NULL,NULL,'Short break','pending',NULL,NOW()-INTERVAL '1 day');
  END IF;

  -- Leo – approved sick (with cert)
  IF NOT EXISTS (SELECT 1 FROM "LeaveRequests" WHERE "EmployeeId" = v_leo AND "LeaveType" = 'sick') THEN
    INSERT INTO "LeaveRequests" ("Id","EmployeeId","LeaveType","StartDate","EndDate","StartTime","EndTime","Notes","Status","MedicalCertUrl","CreatedAt")
    VALUES (gen_random_uuid(),v_leo,'sick',
      NOW()-INTERVAL '8 days', NOW()-INTERVAL '7 days',NULL,NULL,'MC submitted','approved',
      'https://example.com/mc/leo-rahman.pdf',NOW()-INTERVAL '8 days');
  END IF;

  -- Alex – half-day leave
  IF NOT EXISTS (SELECT 1 FROM "LeaveRequests" WHERE "EmployeeId" = v_alex AND "LeaveType" = 'annual') THEN
    INSERT INTO "LeaveRequests" ("Id","EmployeeId","LeaveType","StartDate","EndDate","StartTime","EndTime","Notes","Status","MedicalCertUrl","CreatedAt")
    VALUES (gen_random_uuid(),v_alex,'annual',
      NOW()-INTERVAL '5 days', NOW()-INTERVAL '5 days','08:00','13:00','Half day — personal errand','approved',NULL,NOW()-INTERVAL '6 days');
  END IF;

  -- ── 9. Leave entitlements ───────────────────────────────────────────────────
  INSERT INTO "LeaveEntitlements" ("Id","EmployeeId","Year","AnnualDays","SickDays")
  SELECT gen_random_uuid(), s."Id", EXTRACT(YEAR FROM NOW())::INT, 14, 14
  FROM "Staff" s
  WHERE NOT EXISTS (
    SELECT 1 FROM "LeaveEntitlements" e
    WHERE e."EmployeeId" = s."Id" AND e."Year" = EXTRACT(YEAR FROM NOW())::INT
  );

  -- ── 10. Tips pools ──────────────────────────────────────────────────────────
  INSERT INTO "TipsPools" ("Id","LocationId","LocationName","ShiftDate","TotalAmount","SplitMethod","IsDistributed","StaffCount","CreatedAt")
  SELECT gen_random_uuid(), v_mainbar,'Main Bar',
    (CURRENT_DATE - (n || ' days')::INTERVAL)::TIMESTAMP AT TIME ZONE 'UTC',
    (350 + (n * 17) % 300)::DECIMAL, 'equal', TRUE, 3, NOW() - (n || ' days')::INTERVAL
  FROM generate_series(1,14,2) AS n
  WHERE NOT EXISTS (
    SELECT 1 FROM "TipsPools" WHERE "LocationId" = v_mainbar
      AND "ShiftDate"::DATE = (CURRENT_DATE - (n || ' days')::INTERVAL)::DATE
  );

  INSERT INTO "TipsPools" ("Id","LocationId","LocationName","ShiftDate","TotalAmount","SplitMethod","IsDistributed","StaffCount","CreatedAt")
  SELECT gen_random_uuid(), v_vip,'VIP Lounge',
    (CURRENT_DATE - (n || ' days')::INTERVAL)::TIMESTAMP AT TIME ZONE 'UTC',
    (500 + (n * 23) % 500)::DECIMAL, 'by_hours', TRUE, 2, NOW() - (n || ' days')::INTERVAL
  FROM generate_series(2,12,3) AS n
  WHERE NOT EXISTS (
    SELECT 1 FROM "TipsPools" WHERE "LocationId" = v_vip
      AND "ShiftDate"::DATE = (CURRENT_DATE - (n || ' days')::INTERVAL)::DATE
  );

  -- Un-distributed tip for today (pending action)
  IF NOT EXISTS (SELECT 1 FROM "TipsPools" WHERE "LocationId" = v_mainbar AND "ShiftDate"::DATE = CURRENT_DATE) THEN
    INSERT INTO "TipsPools" ("Id","LocationId","LocationName","ShiftDate","TotalAmount","SplitMethod","IsDistributed","StaffCount","CreatedAt")
    VALUES (gen_random_uuid(),v_mainbar,'Main Bar',NOW()::DATE::TIMESTAMP AT TIME ZONE 'UTC',425.00,'equal',FALSE,0,NOW());
  END IF;

END $$;
