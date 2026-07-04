export interface ConnectorStopVO { floorId: string; x: number; y: number }
export interface ConnectorVO { id: string; connectorCode: string; connectorType: number; name: string; waitSec: number; travelSecPerFloor: number; stops: ConnectorStopVO[] }
export interface ConnectorCreate { siteId: string; connectorCode: string; connectorType: number; name: string; waitSec: number; travelSecPerFloor: number }
export interface ConnectorUpdate { name: string; connectorType: number; waitSec: number; travelSecPerFloor: number }
