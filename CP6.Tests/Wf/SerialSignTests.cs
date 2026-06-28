using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;

namespace CP6.Tests.Wf;

public class SerialSignTests
{
    [Fact]
    public void NewColumns_DefaultToZeroOrNull()
    {
        var task = new Wf_FlowTask();
        Assert.Equal(0, task.StageIndex);
        Assert.Equal(0, task.StageRound);
        var token = new Wf_FlowToken();
        Assert.Null(token.StagePlanJson);
        var formto = new Wf_FlowFormTo();
        Assert.Null(formto.StageIndex);
        Assert.Null(formto.StageRound);
    }

    [Fact]
    public void Constants_AndSentBackStatus_Exist()
    {
        Assert.Equal("fixed", ApprovalStageKinds.Fixed);
        Assert.Equal("managerChain", ApprovalStageKinds.ManagerChain);
        Assert.Equal("all", CountersignModes.All);
        Assert.Equal(7, FlowFormToStatus.SentBack);
    }

    [Fact]
    public void FlowNode_Stages_DefaultsNull()
    {
        Assert.Null(new FlowNode().Stages);
        var stage = new ApprovalStage { Kind = "fixed", ApproverStrategy = "Specified", Countersign = "all" };
        Assert.Equal("fixed", stage.Kind);
    }
}
