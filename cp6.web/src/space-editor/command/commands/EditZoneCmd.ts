import type { Command, EditorContext } from '../Command'

/** Zone 非几何属性补丁（改名/改码/类型/颜色）。polygon 不在此列——波5 不做几何编辑。 */
export type ZonePatch = Partial<{
  zoneName: string
  zoneCode: string
  zoneType: number
  color: string | null
}>

/** EditZoneCmd —— 照 EditMarkerCmd 同构：prev/next 快照 do/undo（undo/redo 生效）。 */
export class EditZoneCmd implements Command {
  label = 'EditZone'

  constructor(
    private zoneId: string,
    private before: ZonePatch,
    private after: ZonePatch,
  ) {}

  private apply(ctx: EditorContext, patch: ZonePatch): void {
    const z = ctx.scene.zones.find(z => z.id === this.zoneId)
    if (!z) return
    if (patch.zoneName !== undefined) z.zoneName = patch.zoneName
    if (patch.zoneCode !== undefined) z.zoneCode = patch.zoneCode
    if (patch.zoneType !== undefined) z.zoneType = patch.zoneType
    if (patch.color !== undefined) z.color = patch.color
    ctx.markDirty(this.zoneId)
  }

  do(ctx: EditorContext): void {
    this.apply(ctx, this.after)
  }

  undo(ctx: EditorContext): void {
    this.apply(ctx, this.before)
  }
}
