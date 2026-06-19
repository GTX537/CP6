namespace CP6.Entity.DomainModels.Fin;

/// <summary>折旧方法（A3-D1 四法）。</summary>
public enum DepreciationMethod { StraightLine = 1, DoubleDeclining = 2, SumOfYears = 3, UnitsOfProduction = 4 }

/// <summary>资产卡片状态。</summary>
public enum AssetStatus { Draft = 0, InUse = 1, FullyDepreciated = 2, Disposed = 3 }

/// <summary>折旧批次状态。</summary>
public enum DepreciationRunStatus { Draft = 0, Posted = 1, Reversed = 2 }

/// <summary>折旧批次生成路径。DisposalFinal=处置补提单资产 Run，不计入「每期单批」FA003。</summary>
public enum DepreciationRunMode { Manual = 1, Worker = 2, CloseHook = 3, DisposalFinal = 4 }

/// <summary>处置类型。</summary>
public enum AssetDisposalType { Sale = 1, Scrap = 2, Transfer = 3, InventoryLoss = 4 }

/// <summary>处置单状态。</summary>
public enum AssetDisposalStatus { Draft = 0, Confirmed = 1, Reversed = 2 }
