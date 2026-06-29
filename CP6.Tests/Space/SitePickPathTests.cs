using CP6.WebApi.Controllers.Space;
using Xunit;

namespace CP6.Tests.Space;

public class SitePickPathTests
{
    [Fact]
    public void ComputeFloorZ_accumulates_by_level_ascending_from_zero()
    {
        var f1 = Guid.NewGuid(); var f2 = Guid.NewGuid(); var f3 = Guid.NewGuid();
        var floors = new List<(Guid FloorId, int Level, int Height)>
        {
            (f2, 2, 5000),
            (f1, 1, 6000),
            (f3, 3, 4000),
        };
        var z = SpaceAdvancedController.ComputeFloorZ(floors);
        Assert.Equal(0, z[f1]);       // Level1 → 0
        Assert.Equal(6000, z[f2]);    // Level2 → 6000
        Assert.Equal(11000, z[f3]);   // Level3 → 6000+5000
    }
}
