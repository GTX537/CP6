// 多通貨・整理（フォルダ階層化）後、サブ名前空間に分割したエンティティ／DTO を
// プロジェクト内のどこからでも解決できるようにする global using 集約。
// 物理フォルダ = 名前空間（Erp / Sys / Integration / Common / Mes / Wms）。
global using CP6.Entity.DomainModels.Erp;
global using CP6.Entity.DomainModels.Sys;
global using CP6.Entity.DomainModels.Integration;
global using CP6.Entity.DomainModels.Common;
global using CP6.Entity.DTOs.Erp;
global using CP6.Entity.DTOs.Integration;
global using CP6.Entity.DTOs.Sys;
