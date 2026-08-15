import type {
  EditorCommandInput,
  EditorHistoryEntry,
} from './editorBatchCommands'

export interface CadSavedCommand {
  type: string
  targetLogicalId: string
  updateProperties?: unknown
}

export interface CadApplyHistoryResponse {
  appliedChangeCount: number
  undoCommands: readonly CadSavedCommand[]
  redoCommands: readonly CadSavedCommand[]
}

export function buildCadApplyHistoryEntry(
  response: CadApplyHistoryResponse,
): EditorHistoryEntry {
  if (!Number.isInteger(response.appliedChangeCount)
    || response.appliedChangeCount < 1
    || response.appliedChangeCount > 100
    || response.undoCommands.length !== response.appliedChangeCount
    || response.redoCommands.length !== response.appliedChangeCount) {
    throw new Error('CAD Apply history command count is invalid')
  }
  return {
    label: `合入 ${response.appliedChangeCount} 项 CAD 变更`,
    undo: response.undoCommands.map(parseCommand),
    redo: response.redoCommands.map(parseCommand),
  }
}

function parseCommand(command: CadSavedCommand): EditorCommandInput {
  if (!command.targetLogicalId?.trim()) {
    throw new Error('CAD Apply history target identity is missing')
  }
  switch (command.type) {
    case 'DeleteObject':
    case 'RestoreLogicalObject':
      if (command.updateProperties !== undefined) {
        throw new Error('CAD lifecycle history command has an unexpected payload')
      }
      return {
        type: command.type,
        targetLogicalId: command.targetLogicalId,
      }
    case 'UpdateProperties':
      if (!isRecord(command.updateProperties)) {
        throw new Error('CAD update history command is missing its snapshot')
      }
      return {
        type: command.type,
        targetLogicalId: command.targetLogicalId,
        updateProperties: command.updateProperties,
      }
    default:
      throw new Error(`Unsupported CAD Apply history command '${command.type}'`)
  }
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return Boolean(value) && typeof value === 'object' && !Array.isArray(value)
}
