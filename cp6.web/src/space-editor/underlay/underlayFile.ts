import { SpaceSourceType } from '../../../../sdk/typescript/space-design-v1/spaceDesignV1Client'

export function sourceTypeForUnderlay(file: Pick<File, 'name' | 'type'>): SpaceSourceType {
  const extension = file.name.split('.').pop()?.toLowerCase()
  const contentType = file.type.toLowerCase()
  if (extension === 'pdf' && (!contentType || contentType === 'application/pdf')) {
    return SpaceSourceType._2
  }
  if (extension === 'png' && (!contentType || contentType === 'image/png')) {
    return SpaceSourceType._3
  }
  if (
    (extension === 'jpg' || extension === 'jpeg') &&
    (!contentType || contentType === 'image/jpeg')
  ) {
    return SpaceSourceType._4
  }
  throw new Error('Unsupported underlay file')
}
