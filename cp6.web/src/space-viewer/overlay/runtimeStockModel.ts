import { isUsableDataSource } from '@/types/space/dataSource'
import type {
  RuntimeLocationRef,
  RuntimeStockItem,
  SpaceRuntimeInventoryResponse,
} from '@/types/space/runtime'

interface MutableStock {
  item: RuntimeStockItem
  materials: Set<string>
  topQuantity: number
}

export function aggregateRuntimeStock(
  response: SpaceRuntimeInventoryResponse,
  locations: readonly RuntimeLocationRef[],
): RuntimeStockItem[] {
  if (!isUsableDataSource(response.source)) return []

  const byId = new Map<string, MutableStock>()
  for (const location of locations) {
    if (byId.has(location.locationLogicalId)) continue
    byId.set(location.locationLogicalId, {
      item: {
        locationLogicalId: location.locationLogicalId,
        locationCode: location.locationCode,
        binStatus: 0,
        qty: 0,
        allocatedQty: 0,
        capacity: null,
        topMaterial: null,
        productKinds: 0,
      },
      materials: new Set<string>(),
      topQuantity: Number.NEGATIVE_INFINITY,
    })
  }

  for (const row of response.items) {
    const stock = byId.get(row.locationLogicalId)
    if (!stock) continue
    stock.item.qty += row.physicalQuantity
    stock.item.allocatedQty += row.allocatedQuantity
    if (row.materialNumber) {
      stock.materials.add(row.materialNumber)
      if (
        row.physicalQuantity > stock.topQuantity ||
        (row.physicalQuantity === stock.topQuantity &&
          (stock.item.topMaterial === null || row.materialNumber < stock.item.topMaterial))
      ) {
        stock.item.topMaterial = row.materialNumber
        stock.topQuantity = row.physicalQuantity
      }
    }
  }

  return [...byId.values()].map((stock) => ({
    ...stock.item,
    binStatus: stock.item.qty > 0 ? 1 : 0,
    productKinds: stock.materials.size,
  }))
}
