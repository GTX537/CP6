import http from '../http'
import type { NativeDeadlinePolicy } from './nativeInbox'

export interface WorkCalendarDay { date: string; isWorkday: boolean; note?: string | null }
export interface HolidayPackage { version: string; sha256: string; years: number[] }
export interface WorkCalendarYear {
  calendarId: string; revision: number; recordedAt: string | null; origin: string; year: number
  weekRule: string; items: WorkCalendarDay[]; timeZoneId: string | null; timeZoneRulesVersion: string | null
  timeZoneSource: string; timeZoneFailure: string | null; japaneseHolidays: HolidayPackage
}
export interface DeadlineEvidence {
  revision: number; occurrence: number; state: string; createdAt: string
  dueAt: string | null; nextDue: string | null; policy: NativeDeadlinePolicy
}
export interface RecalculationItem {
  taskId: string; taskVersion: string; instanceVersion: string; before: DeadlineEvidence; after: DeadlineEvidence
}
export interface RecalculationPreview {
  previewId: string; calendarId: string; calendarRevision: number; timeZoneRulesVersion: string
  expiresAt: string; items: RecalculationItem[]
}
interface CalendarCommand { commandId: string; calendarId: string; expectedRevision: number }
export type CalendarIntent =
  | { kind: 'changes'; command: CalendarCommand & { date: string; isWorkday: boolean | null; note: string | null } }
  | { kind: 'import-jp'; command: CalendarCommand & { year: number } }
  | { kind: 'deadline-recalculations'; command: { commandId: string; previewId: string; taskId: string; reason: string
    expectedDeadlineRevision: number; expectedTaskVersion: string; expectedInstanceVersion: string } }
export interface CalendarReceipt { commandId: string; inserted?: string[]; skipped?: string[]; after?: DeadlineEvidence }

const root = '/oa/work-calendar/versions'
const unwrap = <T>(res: any): T => res?.data ?? res

export const workCalendarApi = {
  list: async (year: number, revision?: number, signal?: AbortSignal) =>
    unwrap<WorkCalendarYear>(await http.get(root, { params: { year, revision }, signal })),
  execute: async (intent: CalendarIntent, signal?: AbortSignal) =>
    unwrap<CalendarReceipt>(await http.post(root + '/' + intent.kind, intent.command, { signal })),
  preview: async (calendarId: string, expectedCalendarRevision: number, taskIds: string[], signal?: AbortSignal) =>
    unwrap<RecalculationPreview>(await http.post(root + '/deadline-previews', { calendarId, expectedCalendarRevision, taskIds }, { signal })),
}
