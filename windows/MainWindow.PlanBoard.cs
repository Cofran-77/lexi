using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Lexi.Features.Learning;
using Lexi.Features.Settings;

namespace Lexi;

public partial class MainWindow
{
    private SettingsDrawerControl? _planEditDrawer;

    private void RenderPlanBoardGroup(IEnumerable<DailyStudyPlan> plans)
    {
        var board=new PlanBoardControl();
        var grid=new ResponsivePlanGrid();
        var today=DateOnly.FromDateTime(DateTime.Now);
        foreach(var plan in plans)
        {
            var todayCompleted = plan.LastBatchCompletedDate is { } completed && completed < today
                ? 0 : plan.CurrentBatchWordIds.Count(id => plan.CompletedWordIds.Contains(id));
            var target=plan.LastBatchCompletedDate is { } previous && previous < today
                ? Math.Min(plan.DailyWordCount,plan.Words.Count-plan.CompletedWordIds.Count)
                : plan.CurrentBatchWordIds.Count>0?plan.CurrentBatchWordIds.Count:Math.Min(plan.DailyWordCount,plan.Words.Count-plan.CompletedWordIds.Count);
            var model=new PlanCardModel(plan.Id,plan.Name,plan.SourceLabel,
                UiText.Text(plan.Status==DailyStudyPlanStatus.Active?"进行中":plan.Status==DailyStudyPlanStatus.Stopped?"已停止":"已完成"),
                todayCompleted,target,
                plan.CompletedWordIds.Count,plan.Words.Count,DailyStudyPlanRules.EstimatedDaysRemaining(plan),plan.Status==DailyStudyPlanStatus.Active,plan);
            var card=board.CreateStandardCard(model,
                _=> { if(plan.Status==DailyStudyPlanStatus.Stopped) ResumeLearningPlan(plan); else if(plan.Status==DailyStudyPlanStatus.Completed) ShowLastPlanBatch(plan); else {ShowPage("learning");StartDailyLearning(plan);} },
                (_,anchor)=>ShowPlanManagementMenu(plan,anchor));
            if(plan.Status!=DailyStudyPlanStatus.Active && card is Border { Child: Grid layout } && layout.Children.LastOrDefault() is Grid actionBar && actionBar.Children[0] is Button start)
            { start.Content=UiText.Text(plan.Status==DailyStudyPlanStatus.Stopped?"恢复计划":"查看最近批次"); start.IsEnabled=plan.Status==DailyStudyPlanStatus.Stopped||plan.CurrentBatchWordIds.Count>0; }
            grid.Children.Add(card);
        }
        var cards=grid.Children.ToArray();
        grid.Children.Clear();
        board.SetCards(cards);
        _learningPlansPanel.Children.Add(board);
    }

    private void ShowPlanManagementMenu(DailyStudyPlan plan,Control anchor)
    {
        var menu=new MenuFlyout();
        TrackStudyFlyout(menu);
        void Item(string title,Action action){var item=new MenuItem { Header=UiText.Text(title) }; item.Click+=(_,_)=>action(); menu.Items.Add(item);}
        if(plan.Status==DailyStudyPlanStatus.Active)
        { Item("调整计划",()=>OpenPlanEditor(plan)); Item("停止计划",()=>SetLearningPlanStopped(plan)); }
        else if(plan.Status==DailyStudyPlanStatus.Stopped) Item("恢复计划",()=>ResumeLearningPlan(plan));
        if(plan.CurrentBatchWordIds.Count>0) Item("最近批次拼写 / 默写",()=>ShowLastPlanBatch(plan));
        if(plan.Status!=DailyStudyPlanStatus.Active) Item("删除计划…",()=>ConfirmDeletePlan(plan,anchor));
        menu.ShowAt(anchor);
    }

    private void OpenPlanEditor(DailyStudyPlan plan)
    {
        if(_planEditDrawer!=null) ((Grid)RootWindowBorder.Child!).Children.Remove(_planEditDrawer);
        _planEditDrawer=new SettingsDrawerControl();
        _planEditDrawer.SetValue(Panel.ZIndexProperty,210);
        ((Grid)RootWindowBorder.Child!).Children.Add(_planEditDrawer);
        var content=new StackPanel {Spacing=12};
        var name=new TextBox {Text=plan.Name,Watermark=UiText.Text("计划名称")};
        var quota=new TextBox {Text=plan.DailyWordCount.ToString(),Watermark=UiText.Text("每日词数")};
        var random=new CheckBox {Content=UiText.Text("未来批次随机"),IsChecked=plan.RandomOrder};
        content.Children.Add(LearningText(UiText.Text("调整计划"),22));
        content.Children.Add(name); content.Children.Add(quota); content.Children.Add(random);
        var error=new TextBlock {TextWrapping=Avalonia.Media.TextWrapping.Wrap}; content.Children.Add(error);
        content.Children.Add(LearningText(UiText.Text("调整在未来批次生效，已完成词保留。"),12));
        content.Children.Add(LearningButton("保存调整",()=>
        {
            if(string.IsNullOrWhiteSpace(name.Text)||!int.TryParse(quota.Text,out var count)||count<1||count>10000)
            {error.Text=UiText.Text("请填写名称，每日词数为 1–10000。 ");return;}
            var original=_learningPlans;
            AdjustLearningPlan(plan,name.Text,count,random.IsChecked==true);
            if(!ReferenceEquals(original,_learningPlans)) _planEditDrawer.Close();
            else error.Text=GlobalStatusText.Text;
        },true));
        _planEditDrawer.RegisterSectionContent(SettingsSection.StudyAndShortcuts,content);
        _planEditDrawer.Open(SettingsSection.StudyAndShortcuts);
    }

    private void ConfirmDeletePlan(DailyStudyPlan plan,Control anchor)
    {
        var body=new StackPanel {Spacing=12};
        body.Children.Add(LearningText(UiText.Text("删除只清除此计划记录，词汇档案不受影响。")));
        var confirm=new Flyout {Content=body};
        TrackStudyFlyout(confirm);
        body.Children.Add(LearningButton("确认删除此计划",()=>
        {
            var updated=_learningPlans.Where(p=>p.Id!=plan.Id).ToList();
            if(!SaveLearningPlans(updated))return;
            _learningPlans=updated;
            if(_activeLearningPlan?.Id==plan.Id) {_activeLearningPlan=null;_dailyLearningSession=null;_studyWorkspaceHost.Children.Clear();_studyWorkspaceHost.IsVisible=false;_managementHost.IsVisible=true;}
            confirm.Hide(); RenderLearningPlans();
        }));
        body.Children.Add(LearningButton("取消",()=>confirm.Hide()));
        confirm.ShowAt(anchor);
    }
}
