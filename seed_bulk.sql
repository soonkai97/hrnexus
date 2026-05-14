-- ============================================================
--  NightCrew BULK Demo Seed  (idempotent)
-- ============================================================
DO $$
DECLARE
  v_marcus UUID; v_alex UUID; v_sam  UUID;
  v_jay    UUID; v_nina UUID; v_kai  UUID; v_tasha UUID; v_leo UUID;
  v_mainbar UUID; v_vip UUID; v_stage UUID; v_entrance UUID;
BEGIN

  -- Resolve staff
  SELECT "Id" INTO v_marcus FROM "Staff" WHERE "Email"='staff@venue.com'       LIMIT 1;
  SELECT "Id" INTO v_alex   FROM "Staff" WHERE "Email"='manager@venue.com'     LIMIT 1;
  SELECT "Id" INTO v_sam    FROM "Staff" WHERE "Email"='admin@venue.com'       LIMIT 1;
  SELECT "Id" INTO v_jay    FROM "Staff" WHERE "Email"='jay.park@venue.com'    LIMIT 1;
  SELECT "Id" INTO v_nina   FROM "Staff" WHERE "Email"='nina.james@venue.com'  LIMIT 1;
  SELECT "Id" INTO v_kai    FROM "Staff" WHERE "Email"='kai.wong@venue.com'    LIMIT 1;
  SELECT "Id" INTO v_tasha  FROM "Staff" WHERE "Email"='tasha.li@venue.com'    LIMIT 1;
  SELECT "Id" INTO v_leo    FROM "Staff" WHERE "Email"='leo.rahman@venue.com'  LIMIT 1;

  -- Resolve locations
  SELECT "Id" INTO v_mainbar  FROM "Locations" WHERE "Name"='Main Bar'   LIMIT 1;
  SELECT "Id" INTO v_vip      FROM "Locations" WHERE "Name"='VIP Lounge' LIMIT 1;
  SELECT "Id" INTO v_stage    FROM "Locations" WHERE "Name"='Main Stage' LIMIT 1;
  SELECT "Id" INTO v_entrance FROM "Locations" WHERE "Name"='Entrance'   LIMIT 1;

  -- ── WORK LOGS  (90 days, Fri/Sat/Sun + some weekdays) ──────────────────────
  -- Marcus – bartender, Main Bar, works 5 nights/week
  INSERT INTO "WorkLogs"("Id","EmployeeId","RoleName","LocationId","LocationName","WorkDate","StartTime","EndTime","IsManualEntry","Notes","HourlyRateSnapshot","CreatedAt")
  SELECT gen_random_uuid(), v_marcus,'bartender',v_mainbar,'Main Bar',
         d::DATE,
         '20:00',
         CASE WHEN EXTRACT(DOW FROM d) IN (5,6) THEN '04:30' ELSE '02:30' END,
         FALSE, NULL, 18.00, d + INTERVAL '21 hours'
  FROM generate_series(CURRENT_DATE-89, CURRENT_DATE-1, '1 day'::INTERVAL) d
  WHERE EXTRACT(DOW FROM d) IN (2,3,4,5,6)   -- Tue-Sat
    AND NOT EXISTS(SELECT 1 FROM "WorkLogs" WHERE "EmployeeId"=v_marcus AND "WorkDate"=d::DATE);

  -- Alex – manager, rotates between VIP and Main Bar
  INSERT INTO "WorkLogs"("Id","EmployeeId","RoleName","LocationId","LocationName","WorkDate","StartTime","EndTime","IsManualEntry","Notes","HourlyRateSnapshot","CreatedAt")
  SELECT gen_random_uuid(), v_alex,'manager',
         CASE WHEN EXTRACT(DOW FROM d) IN (5,6) THEN v_vip ELSE v_mainbar END,
         CASE WHEN EXTRACT(DOW FROM d) IN (5,6) THEN 'VIP Lounge' ELSE 'Main Bar' END,
         d::DATE,'19:00','03:00',FALSE,NULL,22.00, d + INTERVAL '20 hours'
  FROM generate_series(CURRENT_DATE-89, CURRENT_DATE-1, '1 day'::INTERVAL) d
  WHERE EXTRACT(DOW FROM d) IN (1,3,4,5,6)   -- Mon, Wed-Sat
    AND NOT EXISTS(SELECT 1 FROM "WorkLogs" WHERE "EmployeeId"=v_alex AND "WorkDate"=d::DATE);

  -- Jay – bouncer, Entrance, 4 nights/week with some OT shifts
  INSERT INTO "WorkLogs"("Id","EmployeeId","RoleName","LocationId","LocationName","WorkDate","StartTime","EndTime","IsManualEntry","Notes","HourlyRateSnapshot","CreatedAt")
  SELECT gen_random_uuid(), v_jay,'bouncer',v_entrance,'Entrance',
         d::DATE,'21:00',
         CASE WHEN EXTRACT(DOW FROM d) IN (5,6) THEN '05:30' ELSE '03:30' END,
         FALSE,
         CASE WHEN EXTRACT(DOW FROM d)=6 THEN 'Weekend extended shift' ELSE NULL END,
         20.00, d + INTERVAL '22 hours'
  FROM generate_series(CURRENT_DATE-89, CURRENT_DATE-1, '1 day'::INTERVAL) d
  WHERE EXTRACT(DOW FROM d) IN (3,4,5,6)     -- Wed-Sat
    AND NOT EXISTS(SELECT 1 FROM "WorkLogs" WHERE "EmployeeId"=v_jay AND "WorkDate"=d::DATE);

  -- Nina – waitstaff, VIP Lounge, 3 nights + some manual entries
  INSERT INTO "WorkLogs"("Id","EmployeeId","RoleName","LocationId","LocationName","WorkDate","StartTime","EndTime","IsManualEntry","Notes","HourlyRateSnapshot","CreatedAt")
  SELECT gen_random_uuid(), v_nina,'waitstaff',v_vip,'VIP Lounge',
         d::DATE,'20:30','03:00',
         CASE WHEN EXTRACT(DOW FROM d)=2 THEN TRUE ELSE FALSE END,
         CASE WHEN EXTRACT(DOW FROM d)=2 THEN 'Manual correction' ELSE NULL END,
         15.00, d + INTERVAL '21 hours'
  FROM generate_series(CURRENT_DATE-89, CURRENT_DATE-1, '1 day'::INTERVAL) d
  WHERE EXTRACT(DOW FROM d) IN (2,4,5,6)     -- Tue, Thu-Sat
    AND NOT EXISTS(SELECT 1 FROM "WorkLogs" WHERE "EmployeeId"=v_nina AND "WorkDate"=d::DATE);

  -- Kai – DJ, Main Stage, weekends only + special events
  INSERT INTO "WorkLogs"("Id","EmployeeId","RoleName","LocationId","LocationName","WorkDate","StartTime","EndTime","IsManualEntry","Notes","HourlyRateSnapshot","CreatedAt")
  SELECT gen_random_uuid(), v_kai,'dj',v_stage,'Main Stage',
         d::DATE,'22:00','05:00',FALSE,
         CASE WHEN EXTRACT(DOW FROM d)=6 THEN 'Main set + encore' ELSE 'Opening set' END,
         25.00, d + INTERVAL '23 hours'
  FROM generate_series(CURRENT_DATE-89, CURRENT_DATE-1, '1 day'::INTERVAL) d
  WHERE EXTRACT(DOW FROM d) IN (4,5,6)       -- Thu-Sat
    AND NOT EXISTS(SELECT 1 FROM "WorkLogs" WHERE "EmployeeId"=v_kai AND "WorkDate"=d::DATE);

  -- Tasha – bartender, alternates Main Bar / VIP
  INSERT INTO "WorkLogs"("Id","EmployeeId","RoleName","LocationId","LocationName","WorkDate","StartTime","EndTime","IsManualEntry","Notes","HourlyRateSnapshot","CreatedAt")
  SELECT gen_random_uuid(), v_tasha,'bartender',
         CASE WHEN EXTRACT(DOW FROM d)=6 THEN v_vip ELSE v_mainbar END,
         CASE WHEN EXTRACT(DOW FROM d)=6 THEN 'VIP Lounge' ELSE 'Main Bar' END,
         d::DATE,'19:30',
         CASE WHEN EXTRACT(DOW FROM d) IN (5,6) THEN '04:00' ELSE '02:00' END,
         FALSE,NULL,18.00, d + INTERVAL '20 hours'
  FROM generate_series(CURRENT_DATE-89, CURRENT_DATE-1, '1 day'::INTERVAL) d
  WHERE EXTRACT(DOW FROM d) IN (1,3,5,6)     -- Mon, Wed, Fri-Sat
    AND NOT EXISTS(SELECT 1 FROM "WorkLogs" WHERE "EmployeeId"=v_tasha AND "WorkDate"=d::DATE);

  -- Leo – bouncer, Entrance, long OT shifts on weekends
  INSERT INTO "WorkLogs"("Id","EmployeeId","RoleName","LocationId","LocationName","WorkDate","StartTime","EndTime","IsManualEntry","Notes","HourlyRateSnapshot","CreatedAt")
  SELECT gen_random_uuid(), v_leo,'bouncer',v_entrance,'Entrance',
         d::DATE,'20:00',
         CASE WHEN EXTRACT(DOW FROM d) IN (5,6) THEN '06:00' ELSE '03:00' END,
         FALSE,
         CASE WHEN EXTRACT(DOW FROM d) IN (5,6) THEN 'VIP event – extended' ELSE NULL END,
         20.00, d + INTERVAL '21 hours'
  FROM generate_series(CURRENT_DATE-89, CURRENT_DATE-1, '1 day'::INTERVAL) d
  WHERE EXTRACT(DOW FROM d) IN (4,5,6,0)     -- Thu-Sun
    AND NOT EXISTS(SELECT 1 FROM "WorkLogs" WHERE "EmployeeId"=v_leo AND "WorkDate"=d::DATE);

  -- ── LEAVE REQUESTS (25 records across all staff) ─────────────────────────────

  -- Marcus
  INSERT INTO "LeaveRequests"("Id","EmployeeId","LeaveType","StartDate","EndDate","StartTime","EndTime","Notes","Status","MedicalCertUrl","CreatedAt")
  SELECT gen_random_uuid(),v_marcus,'annual',
    NOW()-'45 days'::INTERVAL, NOW()-'43 days'::INTERVAL,
    NULL,NULL,'Long weekend trip','approved',NULL,NOW()-'47 days'::INTERVAL
  WHERE NOT EXISTS(SELECT 1 FROM "LeaveRequests" WHERE "EmployeeId"=v_marcus AND "StartDate"::DATE=(NOW()-'45 days'::INTERVAL)::DATE);

  INSERT INTO "LeaveRequests"("Id","EmployeeId","LeaveType","StartDate","EndDate","StartTime","EndTime","Notes","Status","MedicalCertUrl","CreatedAt")
  SELECT gen_random_uuid(),v_marcus,'sick',
    NOW()-'20 days'::INTERVAL, NOW()-'19 days'::INTERVAL,
    NULL,NULL,'Gastroenteritis','approved',NULL,NOW()-'20 days'::INTERVAL
  WHERE NOT EXISTS(SELECT 1 FROM "LeaveRequests" WHERE "EmployeeId"=v_marcus AND "LeaveType"='sick' AND "StartDate"::DATE=(NOW()-'20 days'::INTERVAL)::DATE);

  INSERT INTO "LeaveRequests"("Id","EmployeeId","LeaveType","StartDate","EndDate","StartTime","EndTime","Notes","Status","MedicalCertUrl","CreatedAt")
  SELECT gen_random_uuid(),v_marcus,'annual',
    NOW()+'14 days'::INTERVAL, NOW()+'16 days'::INTERVAL,
    NULL,NULL,'Holiday','pending',NULL,NOW()-'2 days'::INTERVAL
  WHERE NOT EXISTS(SELECT 1 FROM "LeaveRequests" WHERE "EmployeeId"=v_marcus AND "StartDate"::DATE=(NOW()+'14 days'::INTERVAL)::DATE);

  -- Alex
  INSERT INTO "LeaveRequests"("Id","EmployeeId","LeaveType","StartDate","EndDate","StartTime","EndTime","Notes","Status","MedicalCertUrl","CreatedAt")
  SELECT gen_random_uuid(),v_alex,'annual',
    NOW()-'60 days'::INTERVAL, NOW()-'55 days'::INTERVAL,
    NULL,NULL,'Annual leave','approved',NULL,NOW()-'62 days'::INTERVAL
  WHERE NOT EXISTS(SELECT 1 FROM "LeaveRequests" WHERE "EmployeeId"=v_alex AND "StartDate"::DATE=(NOW()-'60 days'::INTERVAL)::DATE);

  INSERT INTO "LeaveRequests"("Id","EmployeeId","LeaveType","StartDate","EndDate","StartTime","EndTime","Notes","Status","MedicalCertUrl","CreatedAt")
  SELECT gen_random_uuid(),v_alex,'no-show',
    NOW()-'30 days'::INTERVAL, NOW()-'30 days'::INTERVAL,
    NULL,NULL,'No notice given','approved',NULL,NOW()-'29 days'::INTERVAL
  WHERE NOT EXISTS(SELECT 1 FROM "LeaveRequests" WHERE "EmployeeId"=v_alex AND "LeaveType"='no-show');

  -- Jay
  INSERT INTO "LeaveRequests"("Id","EmployeeId","LeaveType","StartDate","EndDate","StartTime","EndTime","Notes","Status","MedicalCertUrl","CreatedAt")
  SELECT gen_random_uuid(),v_jay,'sick',
    NOW()-'40 days'::INTERVAL, NOW()-'38 days'::INTERVAL,
    NULL,NULL,'Migraine','approved','https://example.com/mc/jay-1.pdf',NOW()-'40 days'::INTERVAL
  WHERE NOT EXISTS(SELECT 1 FROM "LeaveRequests" WHERE "EmployeeId"=v_jay AND "StartDate"::DATE=(NOW()-'40 days'::INTERVAL)::DATE);

  INSERT INTO "LeaveRequests"("Id","EmployeeId","LeaveType","StartDate","EndDate","StartTime","EndTime","Notes","Status","MedicalCertUrl","CreatedAt")
  SELECT gen_random_uuid(),v_jay,'annual',
    NOW()+'5 days'::INTERVAL, NOW()+'8 days'::INTERVAL,
    NULL,NULL,'Family event','pending',NULL,NOW()-'1 day'::INTERVAL
  WHERE NOT EXISTS(SELECT 1 FROM "LeaveRequests" WHERE "EmployeeId"=v_jay AND "StartDate"::DATE=(NOW()+'5 days'::INTERVAL)::DATE);

  INSERT INTO "LeaveRequests"("Id","EmployeeId","LeaveType","StartDate","EndDate","StartTime","EndTime","Notes","Status","MedicalCertUrl","CreatedAt")
  SELECT gen_random_uuid(),v_jay,'unpaid',
    NOW()-'70 days'::INTERVAL, NOW()-'67 days'::INTERVAL,
    NULL,NULL,'Personal leave','rejected',NULL,NOW()-'72 days'::INTERVAL
  WHERE NOT EXISTS(SELECT 1 FROM "LeaveRequests" WHERE "EmployeeId"=v_jay AND "LeaveType"='unpaid');

  -- Nina
  INSERT INTO "LeaveRequests"("Id","EmployeeId","LeaveType","StartDate","EndDate","StartTime","EndTime","Notes","Status","MedicalCertUrl","CreatedAt")
  SELECT gen_random_uuid(),v_nina,'sick',
    NOW()-'10 days'::INTERVAL, NOW()-'9 days'::INTERVAL,
    NULL,NULL,'Flu','pending',NULL,NOW()-'10 days'::INTERVAL
  WHERE NOT EXISTS(SELECT 1 FROM "LeaveRequests" WHERE "EmployeeId"=v_nina AND "StartDate"::DATE=(NOW()-'10 days'::INTERVAL)::DATE);

  INSERT INTO "LeaveRequests"("Id","EmployeeId","LeaveType","StartDate","EndDate","StartTime","EndTime","Notes","Status","MedicalCertUrl","CreatedAt")
  SELECT gen_random_uuid(),v_nina,'annual',
    NOW()-'55 days'::INTERVAL, NOW()-'52 days'::INTERVAL,
    NULL,NULL,'Year-end break','approved',NULL,NOW()-'57 days'::INTERVAL
  WHERE NOT EXISTS(SELECT 1 FROM "LeaveRequests" WHERE "EmployeeId"=v_nina AND "StartDate"::DATE=(NOW()-'55 days'::INTERVAL)::DATE);

  INSERT INTO "LeaveRequests"("Id","EmployeeId","LeaveType","StartDate","EndDate","StartTime","EndTime","Notes","Status","MedicalCertUrl","CreatedAt")
  SELECT gen_random_uuid(),v_nina,'annual',
    NOW()+'20 days'::INTERVAL, NOW()+'24 days'::INTERVAL,
    NULL,NULL,'Travel','pending',NULL,NOW()
  WHERE NOT EXISTS(SELECT 1 FROM "LeaveRequests" WHERE "EmployeeId"=v_nina AND "StartDate"::DATE=(NOW()+'20 days'::INTERVAL)::DATE);

  -- Kai
  INSERT INTO "LeaveRequests"("Id","EmployeeId","LeaveType","StartDate","EndDate","StartTime","EndTime","Notes","Status","MedicalCertUrl","CreatedAt")
  SELECT gen_random_uuid(),v_kai,'annual',
    NOW()-'25 days'::INTERVAL, NOW()-'22 days'::INTERVAL,
    NULL,NULL,'Music festival','approved',NULL,NOW()-'27 days'::INTERVAL
  WHERE NOT EXISTS(SELECT 1 FROM "LeaveRequests" WHERE "EmployeeId"=v_kai AND "StartDate"::DATE=(NOW()-'25 days'::INTERVAL)::DATE);

  INSERT INTO "LeaveRequests"("Id","EmployeeId","LeaveType","StartDate","EndDate","StartTime","EndTime","Notes","Status","MedicalCertUrl","CreatedAt")
  SELECT gen_random_uuid(),v_kai,'sick',
    NOW()-'5 days'::INTERVAL, NOW()-'4 days'::INTERVAL,
    NULL,NULL,'Throat infection','pending',NULL,NOW()-'5 days'::INTERVAL
  WHERE NOT EXISTS(SELECT 1 FROM "LeaveRequests" WHERE "EmployeeId"=v_kai AND "StartDate"::DATE=(NOW()-'5 days'::INTERVAL)::DATE);

  -- Tasha
  INSERT INTO "LeaveRequests"("Id","EmployeeId","LeaveType","StartDate","EndDate","StartTime","EndTime","Notes","Status","MedicalCertUrl","CreatedAt")
  SELECT gen_random_uuid(),v_tasha,'sick',
    NOW()-'35 days'::INTERVAL, NOW()-'33 days'::INTERVAL,
    NULL,NULL,'Food poisoning','approved','https://example.com/mc/tasha-1.pdf',NOW()-'35 days'::INTERVAL
  WHERE NOT EXISTS(SELECT 1 FROM "LeaveRequests" WHERE "EmployeeId"=v_tasha AND "StartDate"::DATE=(NOW()-'35 days'::INTERVAL)::DATE);

  INSERT INTO "LeaveRequests"("Id","EmployeeId","LeaveType","StartDate","EndDate","StartTime","EndTime","Notes","Status","MedicalCertUrl","CreatedAt")
  SELECT gen_random_uuid(),v_tasha,'annual',
    NOW()-'80 days'::INTERVAL, NOW()-'76 days'::INTERVAL,
    NULL,NULL,'CNY break','approved',NULL,NOW()-'82 days'::INTERVAL
  WHERE NOT EXISTS(SELECT 1 FROM "LeaveRequests" WHERE "EmployeeId"=v_tasha AND "StartDate"::DATE=(NOW()-'80 days'::INTERVAL)::DATE);

  INSERT INTO "LeaveRequests"("Id","EmployeeId","LeaveType","StartDate","EndDate","StartTime","EndTime","Notes","Status","MedicalCertUrl","CreatedAt")
  SELECT gen_random_uuid(),v_tasha,'unpaid',
    NOW()+'30 days'::INTERVAL, NOW()+'34 days'::INTERVAL,
    NULL,NULL,'Extended personal leave','pending',NULL,NOW()
  WHERE NOT EXISTS(SELECT 1 FROM "LeaveRequests" WHERE "EmployeeId"=v_tasha AND "StartDate"::DATE=(NOW()+'30 days'::INTERVAL)::DATE);

  -- Leo
  INSERT INTO "LeaveRequests"("Id","EmployeeId","LeaveType","StartDate","EndDate","StartTime","EndTime","Notes","Status","MedicalCertUrl","CreatedAt")
  SELECT gen_random_uuid(),v_leo,'annual',
    NOW()-'50 days'::INTERVAL, NOW()-'47 days'::INTERVAL,
    NULL,NULL,'Public holiday extension','approved',NULL,NOW()-'52 days'::INTERVAL
  WHERE NOT EXISTS(SELECT 1 FROM "LeaveRequests" WHERE "EmployeeId"=v_leo AND "StartDate"::DATE=(NOW()-'50 days'::INTERVAL)::DATE);

  INSERT INTO "LeaveRequests"("Id","EmployeeId","LeaveType","StartDate","EndDate","StartTime","EndTime","Notes","Status","MedicalCertUrl","CreatedAt")
  SELECT gen_random_uuid(),v_leo,'no-show',
    NOW()-'15 days'::INTERVAL, NOW()-'15 days'::INTERVAL,
    NULL,NULL,'Absent without notice','approved',NULL,NOW()-'14 days'::INTERVAL
  WHERE NOT EXISTS(SELECT 1 FROM "LeaveRequests" WHERE "EmployeeId"=v_leo AND "LeaveType"='no-show');

  INSERT INTO "LeaveRequests"("Id","EmployeeId","LeaveType","StartDate","EndDate","StartTime","EndTime","Notes","Status","MedicalCertUrl","CreatedAt")
  SELECT gen_random_uuid(),v_leo,'sick',
    NOW()+'2 days'::INTERVAL, NOW()+'3 days'::INTERVAL,
    NULL,NULL,'Pre-planned medical procedure','pending',NULL,NOW()-'3 days'::INTERVAL
  WHERE NOT EXISTS(SELECT 1 FROM "LeaveRequests" WHERE "EmployeeId"=v_leo AND "StartDate"::DATE=(NOW()+'2 days'::INTERVAL)::DATE);

  -- ── TIPS POOLS  (60 days) ───────────────────────────────────────────────────
  -- Main Bar – nearly every night
  INSERT INTO "TipsPools"("Id","LocationId","LocationName","ShiftDate","TotalAmount","SplitMethod","IsDistributed","StaffCount","CreatedAt")
  SELECT gen_random_uuid(), v_mainbar, 'Main Bar',
         d::TIMESTAMP AT TIME ZONE 'UTC',
         ROUND((200 + (EXTRACT(DOW FROM d)*47 + EXTRACT(DAY FROM d)*13)::DECIMAL % 400), 2),
         'equal', TRUE, 3, d + INTERVAL '8 hours'
  FROM generate_series(CURRENT_DATE-59, CURRENT_DATE-1, '1 day'::INTERVAL) d
  WHERE EXTRACT(DOW FROM d) IN (2,3,4,5,6)
    AND NOT EXISTS(SELECT 1 FROM "TipsPools" WHERE "LocationId"=v_mainbar AND "ShiftDate"::DATE=d::DATE);

  -- VIP Lounge – Fri/Sat bigger tips
  INSERT INTO "TipsPools"("Id","LocationId","LocationName","ShiftDate","TotalAmount","SplitMethod","IsDistributed","StaffCount","CreatedAt")
  SELECT gen_random_uuid(), v_vip, 'VIP Lounge',
         d::TIMESTAMP AT TIME ZONE 'UTC',
         ROUND((400 + (EXTRACT(DOW FROM d)*83 + EXTRACT(DAY FROM d)*29)::DECIMAL % 800), 2),
         'by_hours', TRUE, 2, d + INTERVAL '8 hours'
  FROM generate_series(CURRENT_DATE-59, CURRENT_DATE-1, '1 day'::INTERVAL) d
  WHERE EXTRACT(DOW FROM d) IN (4,5,6)
    AND NOT EXISTS(SELECT 1 FROM "TipsPools" WHERE "LocationId"=v_vip AND "ShiftDate"::DATE=d::DATE);

  -- Main Stage – weekends
  INSERT INTO "TipsPools"("Id","LocationId","LocationName","ShiftDate","TotalAmount","SplitMethod","IsDistributed","StaffCount","CreatedAt")
  SELECT gen_random_uuid(), v_stage, 'Main Stage',
         d::TIMESTAMP AT TIME ZONE 'UTC',
         ROUND((150 + (EXTRACT(DAY FROM d)*37)::DECIMAL % 250), 2),
         'equal', TRUE, 2, d + INTERVAL '9 hours'
  FROM generate_series(CURRENT_DATE-59, CURRENT_DATE-1, '1 day'::INTERVAL) d
  WHERE EXTRACT(DOW FROM d) IN (5,6)
    AND NOT EXISTS(SELECT 1 FROM "TipsPools" WHERE "LocationId"=v_stage AND "ShiftDate"::DATE=d::DATE);

  -- 3 undistributed tips (pending)
  INSERT INTO "TipsPools"("Id","LocationId","LocationName","ShiftDate","TotalAmount","SplitMethod","IsDistributed","StaffCount","CreatedAt")
  SELECT gen_random_uuid(), v_mainbar,'Main Bar', CURRENT_DATE::TIMESTAMP AT TIME ZONE 'UTC', 520.00,'equal',FALSE,0,NOW()
  WHERE NOT EXISTS(SELECT 1 FROM "TipsPools" WHERE "LocationId"=v_mainbar AND "ShiftDate"::DATE=CURRENT_DATE);

  INSERT INTO "TipsPools"("Id","LocationId","LocationName","ShiftDate","TotalAmount","SplitMethod","IsDistributed","StaffCount","CreatedAt")
  SELECT gen_random_uuid(), v_vip,'VIP Lounge', CURRENT_DATE::TIMESTAMP AT TIME ZONE 'UTC', 950.00,'by_hours',FALSE,0,NOW()
  WHERE NOT EXISTS(SELECT 1 FROM "TipsPools" WHERE "LocationId"=v_vip AND "ShiftDate"::DATE=CURRENT_DATE);

  -- ── ANNOUNCEMENTS (5 more) ───────────────────────────────────────────────────
  INSERT INTO "Announcements"("Id","Title","Body","TargetRoles","IsActive","CreatedById","CreatedByName","CreatedAt","ExpiresAt")
  SELECT gen_random_uuid(),'Public Holiday Reminder — May 25',
    'This coming Monday is a public holiday. The venue will open at 10 PM instead of 9 PM. All staff please arrive 30 minutes earlier than your usual start time.',
    '*',TRUE,v_sam,'Sam Chen',NOW()-'5 days'::INTERVAL,NOW()+'10 days'::INTERVAL
  WHERE NOT EXISTS(SELECT 1 FROM "Announcements" WHERE "Title"='Public Holiday Reminder — May 25');

  INSERT INTO "Announcements"("Id","Title","Body","TargetRoles","IsActive","CreatedById","CreatedByName","CreatedAt","ExpiresAt")
  SELECT gen_random_uuid(),'DJ Schedule Update',
    'Kai Wong will be performing two sets this Friday and Saturday: 11 PM and 2 AM. Please coordinate with the sound team.',
    'dj,manager',TRUE,v_alex,'Alex Rivera',NOW()-'2 days'::INTERVAL,NOW()+'5 days'::INTERVAL
  WHERE NOT EXISTS(SELECT 1 FROM "Announcements" WHERE "Title"='DJ Schedule Update');

  INSERT INTO "Announcements"("Id","Title","Body","TargetRoles","IsActive","CreatedById","CreatedByName","CreatedAt","ExpiresAt")
  SELECT gen_random_uuid(),'Fire Drill — Next Wednesday',
    'Mandatory fire drill next Wednesday at 6 PM before opening. All staff must attend. Duration: approximately 20 minutes.',
    '*',TRUE,v_sam,'Sam Chen',NOW()-'1 day'::INTERVAL,NOW()+'8 days'::INTERVAL
  WHERE NOT EXISTS(SELECT 1 FROM "Announcements" WHERE "Title"='Fire Drill — Next Wednesday');

  INSERT INTO "Announcements"("Id","Title","Body","TargetRoles","IsActive","CreatedById","CreatedByName","CreatedAt","ExpiresAt")
  SELECT gen_random_uuid(),'Tips Payout — This Friday',
    'Tips from the past two weeks will be distributed this Friday before the shift. Please verify your hours with Alex if you have any discrepancies.',
    '*',TRUE,v_alex,'Alex Rivera',NOW()-'3 days'::INTERVAL,NOW()+'4 days'::INTERVAL
  WHERE NOT EXISTS(SELECT 1 FROM "Announcements" WHERE "Title"='Tips Payout — This Friday');

  INSERT INTO "Announcements"("Id","Title","Body","TargetRoles","IsActive","CreatedById","CreatedByName","CreatedAt","ExpiresAt")
  SELECT gen_random_uuid(),'Cocktail Menu Launch',
    'New summer cocktail menu launches this weekend. All bartenders must attend a 1-hour briefing on Thursday at 7 PM. Tasting included.',
    'bartender',TRUE,v_alex,'Alex Rivera',NOW()-'4 days'::INTERVAL,NOW()+'3 days'::INTERVAL
  WHERE NOT EXISTS(SELECT 1 FROM "Announcements" WHERE "Title"='Cocktail Menu Launch');

  -- ── STAFF MESSAGES ───────────────────────────────────────────────────────────
  -- Admin → Marcus
  INSERT INTO "StaffMessages"("Id","SenderId","SenderName","SenderIsAdmin","RecipientId","RecipientName","Content","SentAt","ReadAt")
  SELECT gen_random_uuid(),v_sam,'Sam Chen',TRUE,v_marcus,'Marcus Torres',
    'Hi Marcus, please confirm your availability for the VIP event this Saturday. We need all hands on deck.',
    NOW()-'3 days'::INTERVAL, NOW()-'2 days'::INTERVAL
  WHERE NOT EXISTS(SELECT 1 FROM "StaffMessages" WHERE "SenderId"=v_sam AND "RecipientId"=v_marcus);

  -- Marcus → Admin (reply)
  INSERT INTO "StaffMessages"("Id","SenderId","SenderName","SenderIsAdmin","RecipientId","RecipientName","Content","SentAt","ReadAt")
  SELECT gen_random_uuid(),v_marcus,'Marcus Torres',FALSE,v_sam,'Sam Chen',
    'Hi Sam, confirmed! I''ll be there at 7 PM sharp. Should I bring the new uniform?',
    NOW()-'2 days'::INTERVAL + '2 hours'::INTERVAL, NOW()-'2 days'::INTERVAL + '4 hours'::INTERVAL
  WHERE NOT EXISTS(SELECT 1 FROM "StaffMessages" WHERE "SenderId"=v_marcus AND "RecipientId"=v_sam);

  -- Manager → Jay
  INSERT INTO "StaffMessages"("Id","SenderId","SenderName","SenderIsAdmin","RecipientId","RecipientName","Content","SentAt","ReadAt")
  SELECT gen_random_uuid(),v_alex,'Alex Rivera',TRUE,v_jay,'Jay Park',
    'Jay, your sick leave has been noted. Please send in your MC as soon as possible. Get well soon!',
    NOW()-'1 day'::INTERVAL, NULL
  WHERE NOT EXISTS(SELECT 1 FROM "StaffMessages" WHERE "SenderId"=v_alex AND "RecipientId"=v_jay);

  -- Admin → Nina
  INSERT INTO "StaffMessages"("Id","SenderId","SenderName","SenderIsAdmin","RecipientId","RecipientName","Content","SentAt","ReadAt")
  SELECT gen_random_uuid(),v_sam,'Sam Chen',TRUE,v_nina,'Nina James',
    'Nina, your annual leave for next month has been approved. Have a great trip!',
    NOW()-'4 days'::INTERVAL, NOW()-'4 days'::INTERVAL + '1 hour'::INTERVAL
  WHERE NOT EXISTS(SELECT 1 FROM "StaffMessages" WHERE "SenderId"=v_sam AND "RecipientId"=v_nina);

  -- Admin → Tasha
  INSERT INTO "StaffMessages"("Id","SenderId","SenderName","SenderIsAdmin","RecipientId","RecipientName","Content","SentAt","ReadAt")
  SELECT gen_random_uuid(),v_sam,'Sam Chen',TRUE,v_tasha,'Tasha Li',
    'Tasha, you have been assigned to the VIP section this Saturday. Please check the new cocktail menu before your shift.',
    NOW()-'2 days'::INTERVAL, NULL
  WHERE NOT EXISTS(SELECT 1 FROM "StaffMessages" WHERE "SenderId"=v_sam AND "RecipientId"=v_tasha);

  -- Kai → Admin (unread)
  INSERT INTO "StaffMessages"("Id","SenderId","SenderName","SenderIsAdmin","RecipientId","RecipientName","Content","SentAt","ReadAt")
  SELECT gen_random_uuid(),v_kai,'Kai Wong',FALSE,v_sam,'Sam Chen',
    'Hey Sam, is there any chance I can get a slight adjustment on my set time this Friday? I have a sound check at 9 PM.',
    NOW()-'12 hours'::INTERVAL, NULL
  WHERE NOT EXISTS(SELECT 1 FROM "StaffMessages" WHERE "SenderId"=v_kai AND "RecipientId"=v_sam);

  -- Leo → Manager
  INSERT INTO "StaffMessages"("Id","SenderId","SenderName","SenderIsAdmin","RecipientId","RecipientName","Content","SentAt","ReadAt")
  SELECT gen_random_uuid(),v_leo,'Leo Rahman',FALSE,v_alex,'Alex Rivera',
    'Alex, I''ll be coming in 30 minutes early this weekend for the VIP event. Let me know if there''s anything specific to prepare.',
    NOW()-'6 hours'::INTERVAL, NOW()-'5 hours'::INTERVAL
  WHERE NOT EXISTS(SELECT 1 FROM "StaffMessages" WHERE "SenderId"=v_leo AND "RecipientId"=v_alex);

  -- Manager → Kai (unread by Kai)
  INSERT INTO "StaffMessages"("Id","SenderId","SenderName","SenderIsAdmin","RecipientId","RecipientName","Content","SentAt","ReadAt")
  SELECT gen_random_uuid(),v_alex,'Alex Rivera',TRUE,v_kai,'Kai Wong',
    'Kai, no problem. Sound check moved to 9:30 PM. First set starts at 11 PM as scheduled.',
    NOW()-'10 hours'::INTERVAL, NULL
  WHERE NOT EXISTS(SELECT 1 FROM "StaffMessages" WHERE "SenderId"=v_alex AND "RecipientId"=v_kai);

END $$;
