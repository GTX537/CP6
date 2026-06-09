// フォルダ階層化（Erp / Integration / Common ＋ 既存 Mes / Wms）後、
// サブ名前空間に分割した型をプロジェクト内のどこからでも解決できるようにする集約。
global using CP6.Entity.DomainModels.Erp;
global using CP6.Entity.DomainModels.Sys;
global using CP6.Entity.DomainModels.Integration;
global using CP6.Entity.DomainModels.Common;
global using CP6.Entity.DTOs.Erp;
global using CP6.Entity.DTOs.Integration;
global using CP6.Entity.DTOs.Sys;
global using CP6.Entity.DTOs.Mes;
global using CP6.Entity.DTOs.Wms;
global using CP6.Core.Services.Erp;
global using CP6.Core.Services.Integration;
global using CP6.Core.Services.Common;
