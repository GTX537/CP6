export interface ConnectorStopVO { floorId: string; x: number; y: number }
export interface ConnectorVO { id: string; connectorCode: string; connectorType: number; name: string; stops: ConnectorStopVO[] }
export interface ConnectorCreate { siteId: string; connectorCode: string; connectorType: number; name: string }
