import { ipqcSchemas, payloadRoutes, type IpqcContracts } from './ipqcSchemas.g'
import type { PayloadType, R4PayloadSchemaType } from './ipqc.g'
import { assertSchema } from './schema'
import { parseStrictJson } from './strictJson'
export type IpqcRoute = keyof IpqcContracts

/** One explicit native route, strict decoded-name JSON boundary, then complete shape validation. */
export function decodeIpqc<K extends IpqcRoute>(_route: K, json: string): IpqcContracts[K] {
  if (!Object.hasOwn(ipqcSchemas, _route)) throw new TypeError('UNKNOWN_IPQC_ROUTE')
  const value = parseStrictJson(json)
  assertSchema(ipqcSchemas[_route], value)
  if (_route === 'R4FrozenPayload') {
    const frozen = value as IpqcContracts['R4FrozenPayload']
    if (payloadRoutes[frozen.payloadType] !== frozen.schemaType) throw new TypeError('PAYLOAD_SCHEMA_MISMATCH')
  }
  return value as IpqcContracts[K]
}

/** Validates before serialization; caller objects do not bypass the wire contract. */
export function encodeIpqc<K extends IpqcRoute>(route: K, value: IpqcContracts[K]): string {
  if (!Object.hasOwn(ipqcSchemas, route)) throw new TypeError('UNKNOWN_IPQC_ROUTE')
  assertSchema(ipqcSchemas[route], value)
  const json = JSON.stringify(value)
  decodeIpqc(route, json)
  return json
}

export function decodeIpqcPayload<T extends PayloadType>(
  event: T, schemaType: R4PayloadSchemaType, json: string,
): IpqcContracts[(typeof payloadRoutes)[T]] {
  if (!Object.hasOwn(payloadRoutes, event) || payloadRoutes[event] !== schemaType) {
    throw new TypeError('PAYLOAD_SCHEMA_MISMATCH')
  }
  return decodeIpqc(payloadRoutes[event], json)
}
