export type WorkType =
  | 'VENUECLOCK'     // 写钟 — venue clock hours
  | 'TABLERUN'       // 跑台 — per-table run
  | 'OVERTIME'       // 加时 — overtime extension at venue (extra hours after regular shift)
  | 'OVERNIGHT'      // 包夜 — overnight booking
  | 'PRIVATEHOURS'   // 私小时 — private hours with customer
  | 'SELFDEDUCT'     // 自扣 / 自己写钟 — self-funded clock (comes out of hostess's share)
  | 'REST'           // 休息 — rest / off night
  | 'SLOWNIGHT';     // 吃蛋 — zero-earning night ("eating egg" = zero)

export type Settlement =
  | 'SETTLED'        // 清 — fully cleared
  | 'CASH'           // cash — paid in physical cash (distinct from 清)
  | 'BANKIN'         // bankin — transferred to bank account
  | 'NOTCOLLECTED'   // 还没收 — not yet collected
  | 'COMP'           // comped — complimentary / waived
  | 'PENDING';       // (blank in Excel) — awaiting settlement

export type LoanType = 'ADVANCE' | 'REPAYMENT';

export const WORK_TYPE_LABELS: Record<WorkType, string> = {
  VENUECLOCK:   '写钟',
  TABLERUN:     '跑台',
  OVERTIME:     '加时',
  OVERNIGHT:    '包夜',
  PRIVATEHOURS: '私小时',
  SELFDEDUCT:   '自扣',
  REST:         '休息',
  SLOWNIGHT:    '吃蛋',     // Excel uses 吃蛋 ("eating egg"), not 淡场
};

export const SETTLEMENT_LABELS: Record<Settlement, string> = {
  SETTLED:      '清 (Settled)',
  CASH:         'Cash',
  BANKIN:       'Bank In',
  NOTCOLLECTED: '还没收',
  COMP:         'Comp',
  PENDING:      'Pending',
};

/** Excel 备注 values → Settlement enum (used during import / display matching) */
export const SETTLEMENT_FROM_EXCEL: Record<string, Settlement> = {
  '清':     'SETTLED',
  'bankin': 'BANKIN',
  'cash':   'CASH',
  'x':      'SETTLED',     // cross mark = collected
  '收妈咪':  'SETTLED',    // collected by mama-san = settled
  '还没收':  'NOTCOLLECTED',
  '':       'PENDING',     // blank = pending
};

/** Excel 工作地点 keyword → WorkType (used during import) */
export const WORK_TYPE_FROM_EXCEL: Array<{ test: RegExp; type: WorkType }> = [
  { test: /包夜/,                     type: 'OVERNIGHT'     },
  { test: /私小时|私hrs/i,             type: 'PRIVATEHOURS'  },
  { test: /加时/,                     type: 'OVERTIME'      },
  { test: /休息/,                     type: 'REST'          },
  { test: /吃蛋/,                     type: 'SLOWNIGHT'     },
  { test: /自己写钟|我写钟|自扣/,       type: 'SELFDEDUCT'    },
  { test: /跑台/,                     type: 'TABLERUN'      },
  { test: /写钟/,                     type: 'VENUECLOCK'    }, // catch-all for 写钟 last
];

export interface HostessEntryDto {
  id: string;
  staffId: string;
  entryDate: string;       // yyyy-MM-dd
  workType: WorkType;
  venueUnits: number;
  venueRate: number;
  venueEarnings: number;
  privateGross: number;
  privateNet: number;
  settlement: Settlement;
  notes: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface UpsertEntryRequest {
  staffId: string;
  entryDate: string;       // yyyy-MM-dd
  workType: WorkType;
  venueUnits: number;
  venueRate: number;
  venueEarnings: number;
  privateGross: number;
  privateNet: number;
  settlement: Settlement;
  notes: string | null;
}

export interface HostessExpenseDto {
  id: string;
  staffId: string;
  year: number;
  month: number;
  category: string;
  amount: number;
  notes: string | null;
}

export interface UpsertExpenseRequest {
  staffId: string;
  year: number;
  month: number;
  category: string;
  amount: number;
  notes: string | null;
}

export interface HostessLoanDto {
  id: string;
  staffId: string;
  loanDate: string;        // yyyy-MM-dd
  loanType: LoanType;
  amount: number;
  purpose: string | null;
  createdAt: string;
}

export interface UpsertLoanRequest {
  staffId: string;
  loanDate: string;        // yyyy-MM-dd
  loanType: LoanType;
  amount: number;
  purpose: string | null;
}

export interface HostessSummaryDto {
  staffId: string;
  venueEarnings: number;
  privateGross: number;
  privateNet: number;
  entryCount: number;
}
