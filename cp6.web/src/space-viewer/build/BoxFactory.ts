import {
  BoxGeometry,
  EdgesGeometry,
  HemisphereLight,
  DirectionalLight,
  LineSegments,
  LineBasicMaterial,
  Matrix4,
  MeshBasicMaterial,
  MeshLambertMaterial,
  Quaternion,
  Euler,
  Vector3,
  type Object3D,
} from 'three'

export const UNIT_BOX = new BoxGeometry(1, 1, 1)

export const locMaterial = new MeshLambertMaterial({
  color: 0x607d8b,
  vertexColors: false,
})
locMaterial.vertexColors = false

export const rackEdgesMaterial = new LineBasicMaterial({ color: 0x90a4ae })

export const zoneMaterial = new MeshBasicMaterial({
  color: 0x2196f3,
  transparent: true,
  opacity: 0.08,
  depthWrite: false,
})

export function makeRackEdges(): LineSegments {
  const edges = new EdgesGeometry(UNIT_BOX)
  return new LineSegments(edges, rackEdgesMaterial)
}

export function addLights(parent: Object3D): void {
  const hemi = new HemisphereLight(0xffffff, 0x444444, 1.2)
  hemi.position.set(0, 200, 0)
  parent.add(hemi)

  const dir = new DirectionalLight(0xffffff, 0.8)
  dir.position.set(100, 200, 100)
  parent.add(dir)
}

export function makeInstanceMatrix(
  absX: number,
  absY: number,
  absZ: number,
  rotZDegrees: number,
  sizeW: number,
  sizeD: number,
  sizeH: number,
): Matrix4 {
  // All in data/mm space — SceneRoot scale+rotation handles the world conversion.
  // RotationZ contracts use degrees. Size is applied in data axes (X/Y/Z)
  // before SceneRoot maps data Z-up to Three Y-up.
  const m = new Matrix4()
  const position = new Vector3(absX, absY, absZ)
  const quat = new Quaternion().setFromEuler(
    new Euler(0, 0, (rotZDegrees * Math.PI) / 180),
  )
  const scale = new Vector3(sizeW, sizeD, sizeH)
  m.compose(position, quat, scale)
  return m
}
