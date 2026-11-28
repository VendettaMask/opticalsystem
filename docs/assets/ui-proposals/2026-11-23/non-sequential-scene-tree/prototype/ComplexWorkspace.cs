using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using OpticalSystemDesign.Application.Contracts;

sealed class NavigationNode(string key,string label,string icon,int? number=null)
{
    public string Key=key,Label=label,Icon=icon;
    public int? Number=number;
    public List<NavigationNode> Children=[];
    public NavigationNode Add(params NavigationNode[] children){Children.AddRange(children);return this;}
    public IEnumerable<NavigationNode> Walk()=>new[]{this}.Concat(Children.SelectMany(c=>c.Walk()));
}

sealed class ComplexWorkspace
{
    readonly NonSequentialDocumentDto document;
    readonly Scene3Dto originalScene;
    readonly NonSequentialTraceSessionDto session;
    readonly Dictionary<int,NonSequentialObjectRowDto> rows;
    readonly Dictionary<string,TreeViewItem> items=[];
    readonly Dictionary<int,int[]> channels=[];
    readonly TreeView tree=new(){SelectionMode=SelectionMode.Multiple,AutoScrollToSelectedItem=true,Margin=new Thickness(4,5)};
    readonly Border inspector=new();
    readonly TextBlock treeCount=Ui.Text("",12),scope=Ui.Text("",12),title=Ui.Text("",16,true),breadcrumb=Ui.Text("",12);
    readonly TextBlock pathNote=Ui.Text("",12);
    readonly CheckBox rayToggle=new(){Content="光线",IsChecked=true};
    readonly Button scopeAction=new(){Content="恢复整机",Padding=new Thickness(10,3)};
    readonly Grid stageHost=new();
    readonly Grid stage=new(){Background=Brushes.White};
    readonly DataGrid table=new(){IsReadOnly=true,AutoGenerateColumns=false,SelectionMode=DataGridSelectionMode.Extended};
    readonly ToggleButton modelButton=new(){Content="三维模型",IsChecked=true,Padding=new Thickness(13,5)};
    readonly ToggleButton tableButton=new(){Content="对象表",Padding=new Thickness(13,5)};
    readonly StackPanel navTabs=new(){Orientation=Orientation.Horizontal,Spacing=4,Margin=new Thickness(10,0)};
    readonly HashSet<string> expanded=["project","array","channel-4"];
    readonly List<NavigationNode> structure=[];
    NavigationNode? filtered;
    string navigation="结构";
    bool updating;
    bool mixed;
    int isolated;
    public TextBox Search{get;}=new(){PlaceholderText="搜索名称、材料或编号…",MinHeight=32};
    public HashSet<int> SelectedNumbers{get;}=[];
    public Dictionary<string,Guid[]> SavedSelections{get;}=[];
    public int MatchedObjectCount{get;private set;}
    public bool MixedGeometryShown=>mixed;
    public string NavigationRootKey=>filtered?.Key??"";
    public int MaterializedTreeItems=>items.Count;
    public PreviewSceneView View{get;}
    public Grid Root{get;}
    public WrapPanel Commands{get;}
    public ComplexWorkspace(NonSequentialDocumentDto document,Scene3Dto scene,NonSequentialTraceSessionDto session)
    {
        this.document=document; originalScene=scene;this.session=session;rows=document.Objects.ToDictionary(o=>o.ObjectNumber);
        for(var i=1;i<=32;i++) channels[i]=Enumerable.Range((i-1)*13+1,13).ToArray();
        var project=new NavigationNode("project","32通道照明检测平台","nonsequential-mode");
        var array=new NavigationNode("array","通道模块阵列 · 32","layers"); project.Children.Add(array);
        for(var i=1;i<=32;i++)
        {
            var channel=new NavigationNode("channel-"+i,$"通道 {i:00}","layers");array.Children.Add(channel);
            foreach(var number in channels[i].Take(5)) channel.Children.Add(ObjectNode(number));
            var mechanical=new NavigationNode("mechanical-"+i,"支撑与遮光件 · 8","solid-model");
            mechanical.Children.AddRange(channels[i].Skip(5).Select(ObjectNode));channel.Children.Add(mechanical);
        }
        var platform=new NavigationNode("platform","平台支撑 · 16","solid-model"); platform.Children.AddRange(Enumerable.Range(417,16).Select(ObjectNode));project.Children.Add(platform);structure.Add(project);
        View=new(scene); View.ObjectSelected+=n=>SelectNumbers([n]);
        Commands=new WrapPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(14,11),ItemSpacing=9,Children={
            Ui.Command("nonsequential-mode","添加对象 ⌄"),Ui.Command("file-import","导入"),Ui.Command("copy","复制"),Ui.Command("trash-2","删除"),
            Ui.Command("action-run","光线追迹"),Ui.Command("detector-view","探测器"),Ui.Command("action-undo","撤销")}};
        Root=new Grid{ColumnDefinitions=new ColumnDefinitions("310,6,*,6,342"),Margin=new Thickness(0,4,0,0)};
        Root.Children.Add(BuildNavigator()); var split=new GridSplitter{Width=6,HorizontalAlignment=HorizontalAlignment.Stretch};Grid.SetColumn(split,1);Root.Children.Add(split);
        var center=BuildCenter();Grid.SetColumn(center,2);Root.Children.Add(center);var split2=new GridSplitter{Width=6,HorizontalAlignment=HorizontalAlignment.Stretch};Grid.SetColumn(split2,3);Root.Children.Add(split2);Grid.SetColumn(inspector,4);Root.Children.Add(inspector);
        Search.TextChanged+=(_,_)=>RebuildTree();
        tree.SelectionChanged+=(_,_)=>
        {
            if(updating)return;
            var selected=tree.SelectedItems?.OfType<TreeViewItem>().Select(i=>i.Tag).OfType<NavigationNode>().ToArray()??[];
            var numbers=selected.SelectMany(n=>n.Number is int id?new[]{id}:n.Walk().Where(k=>k.Number.HasValue).Select(k=>k.Number!.Value)).Distinct().ToArray();
            if(numbers.Length>0)SelectNumbers(numbers,false);
            else if(selected.FirstOrDefault()?.Key.StartsWith("task-")==true)inspector.Child=TaskProperties();
        };
        RebuildTree(); IsolateChannel(4);SelectNumbers([43]);
    }
    NavigationNode ObjectNode(int n)
    {
        var row=rows[n];var icon=row.Kind switch{NonSequentialObjectKind.Null=>"move",NonSequentialObjectKind.StandardLens=>"lens-prescription",NonSequentialObjectKind.SourceEllipse=>"source-analysis",NonSequentialObjectKind.DetectorRectangle=>"detector-view",_=>"solid-model"};
        var node=new NavigationNode("object-"+row.Id,row.Name,icon,n);
        if(row.Kind==NonSequentialObjectKind.StandardLens)node.Add(new(node.Key+"-front","前表面","report-surface-data",n),new(node.Key+"-back","后表面","report-surface-data",n));
        return node;
    }
    Control BuildNavigator()
    {
        var root=new Grid{RowDefinitions=new RowDefinitions("44,40,44,32,*,38")};
        var heading=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8,Margin=new Thickness(15,0),Children={Ui.Icon("nonsequential-mode"),Ui.Text("工程导航",15,true)}};root.Children.Add(heading);
        foreach(var mode in new[]{"结构","光学","仿真"}){var button=new ToggleButton{Content=mode,IsChecked=mode==navigation,Padding=new Thickness(16,5)};button.Click+=(_,_)=>SetNavigation(mode);navTabs.Children.Add(button);}Grid.SetRow(navTabs,1);root.Children.Add(navTabs);
        Search.Margin=new Thickness(12,5,12,7);Grid.SetRow(Search,2);root.Children.Add(Search);
        treeCount.Margin=new Thickness(15,0);Grid.SetRow(treeCount,3);root.Children.Add(treeCount);
        Grid.SetRow(tree,4);root.Children.Add(tree);
        var selectSet=new Button{Content="保存当前选择集",Margin=new Thickness(12,2),HorizontalAlignment=HorizontalAlignment.Left};selectSet.Click+=(_,_)=>SaveSelection("选择集 "+(SavedSelections.Count+1));Grid.SetRow(selectSet,5);root.Children.Add(selectSet);
        return Ui.Surface(root,new Thickness(0,0,1,0));
    }
    NavigationNode ActiveRoot()
    {
        if(navigation=="结构")return structure[0];
        if(navigation=="仿真")return new NavigationNode("simulation","仿真与结果","action-run").Add(
            new NavigationNode("task-settings","全局设置","system-settings").Add(new("task-wave","工作波长 · 550 nm","source-analysis"),new("task-medium","环境介质 · 空气","materials-library")),
            new NavigationNode("task-layout","布局光线 · 已准备","action-run"),new NavigationNode("task-analysis","照度分析 · 未运行","ray-footprint"),
            new NavigationNode("task-detectors","探测器结果 · 32","detector-view"),new NavigationNode("task-paths","光线路径筛选","ray-path-analysis"));
        NavigationNode Group(string key,string label,string icon,Func<NonSequentialObjectRowDto,bool> match){var n=new NavigationNode(key,label,icon);n.Children.AddRange(rows.Values.Where(match).Select(o=>ObjectNode(o.ObjectNumber)));return n;}
        return new NavigationNode("optical","光学对象与属性","materials-library").Add(
            Group("sources","光源 · 32","source-analysis",o=>o.Parameters is SourceParameters),
            new NavigationNode("materials","光学材料","materials-library").Add(Group("glass","N-BK7 · 64 个对象","materials-library",o=>o.Parameters is StandardLensParameters)),
            Group("detectors","探测器 · 32","detector-view",o=>o.Kind==NonSequentialObjectKind.DetectorRectangle),
            Group("absorbers","吸收结构 · 272","solid-model",o=>o.Kind==NonSequentialObjectKind.Box),
            Group("references","坐标参考 · 32","move",o=>o.Kind==NonSequentialObjectKind.Null));
    }
    public void SetNavigation(string mode)
    {
        navigation=mode;foreach(var b in navTabs.Children.OfType<ToggleButton>())b.IsChecked=b.Content?.ToString()==mode;
        if(mode=="光学"){expanded.Add("optical");expanded.Add("materials");expanded.Add("glass");} if(mode=="仿真")expanded.Add("simulation");
        RebuildTree();if(mode=="仿真")inspector.Child=TaskProperties();
    }
    bool Matches(NonSequentialObjectRowDto row)=>string.IsNullOrWhiteSpace(Search.Text)||row.Name.Contains(Search.Text.Trim(),StringComparison.OrdinalIgnoreCase)||row.Material.Contains(Search.Text.Trim(),StringComparison.OrdinalIgnoreCase)||row.ObjectNumber.ToString()==Search.Text.Trim();
    NavigationNode? Filter(NavigationNode node)
    {
        if(string.IsNullOrWhiteSpace(Search.Text))return node;
        if(node.Number is int n&&Matches(rows[n]))return node;
        var children=node.Children.Select(Filter).Where(n=>n is not null).Cast<NavigationNode>().ToList();
        if(children.Count==0)return null;
        var result=new NavigationNode(node.Key,node.Label,node.Icon,node.Number);result.Children.AddRange(children);return result;
    }
    public bool TreeHasChannelPath(int channel)=>filtered?.Walk().Any(n=>n.Key=="channel-"+channel)==true;
    void RebuildTree()
    {
        updating=true;
        try
        {
            MatchedObjectCount=rows.Values.Count(Matches);treeCount.Text=navigation=="仿真"?"任务、全局设置与结果":string.IsNullOrWhiteSpace(Search.Text)?"432 个对象 · 32 个通道":$"{MatchedObjectCount} 个匹配 · 保留装配路径";
            items.Clear();tree.Items.Clear();filtered=Filter(ActiveRoot());if(filtered is not null)tree.Items.Add(CreateItem(filtered));
        }
        finally{updating=false;}
        if(SelectedNumbers.Count>0)SelectNumbers(SelectedNumbers.ToArray());
    }
    TreeViewItem CreateItem(NavigationNode node)
    {
        var item=new TreeViewItem{Tag=node,Padding=new Thickness(3,6),Header=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8,Children={Ui.Icon(node.Icon),Ui.Text(node.Label)}},IsExpanded=expanded.Contains(node.Key)};
        items[node.Key]=item;
        void Populate(){if(item.Items.Count==1&&item.Items[0] is string){item.Items.Clear();foreach(var child in node.Children)item.Items.Add(CreateItem(child));}}
        if(node.Children.Count>0){item.Items.Add("…");if(item.IsExpanded)Populate();item.PropertyChanged+=(_,e)=>{if(e.Property==TreeViewItem.IsExpandedProperty){if(item.IsExpanded){expanded.Add(node.Key);Populate();}else expanded.Remove(node.Key);}};}
        if(node.Number is int number)
        {
            var menu=new ContextMenu();var isolate=new MenuItem{Header="隔离所在通道"};isolate.Click+=(_,_)=>IsolateChannel((number-1)/13+1);var locate=new MenuItem{Header="定位对象"};locate.Click+=(_,_)=>{SelectNumbers([number]);View.FocusObject(number);};menu.Items.Add(isolate);menu.Items.Add(locate);item.ContextMenu=menu;
        }
        return item;
    }
    TreeViewItem? FindNumber(NavigationNode node,int number)
    {
        if(!node.Walk().Any(n=>n.Number==number))return null;
        var item=items.GetValueOrDefault(node.Key);if(item is null)return null;
        if(node.Number==number)return item;
        item.IsExpanded=true;
        foreach(var child in node.Children){var result=FindNumber(child,number);if(result is not null)return result;}return null;
    }
    public void SelectNumbers(IEnumerable<int> source,bool updateTree=true)
    {
        var numbers=source.Where(rows.ContainsKey).Distinct().ToArray();if(numbers.Length==0)return;
        updating=true;
        try
        {
            SelectedNumbers.Clear();SelectedNumbers.UnionWith(numbers);View.Highlighted.Clear();View.Highlighted.UnionWith(numbers);View.SelectedNumber=numbers[0];
            if(updateTree&&filtered is not null){tree.SelectedItems?.Clear();foreach(var n in numbers){var item=FindNumber(filtered,n);if(item is not null)tree.SelectedItems?.Add(item);}}
            inspector.Child=ObjectProperties(numbers.Select(n=>rows[n]).ToArray());
            breadcrumb.Text=numbers.Length>1?$"多选 {numbers.Length} 个对象":$"通道 {(numbers[0]-1)/13+1:00} / {rows[numbers[0]].Name}";
        }
        finally{updating=false;}
    }
    public void SaveSelection(string name)=>SavedSelections[name]=SelectedNumbers.Select(n=>rows[n].Id).ToArray();
    public void MoveToPresentationFolder(int number,string folderName)
    {
        var root=structure[0];var target=root.Walk().First(n=>n.Number==number);var parent=root.Walk().First(n=>n.Children.Contains(target));parent.Children.Remove(target);
        var folder=root.Children.FirstOrDefault(n=>n.Key=="folder-"+folderName);if(folder is null){folder=new("folder-"+folderName,folderName,"layers");root.Children.Add(folder);}folder.Children.Add(target);RebuildTree();
    }
    public void IsolateChannel(int channel)
    {
        isolated=channel;var nums=channels[channel].ToHashSet();
        var optical=nums.Where(n=>rows[n].Kind!=NonSequentialObjectKind.Box).ToHashSet();
        View.SetScene(originalScene with{Surfaces=originalScene.Surfaces.Where(s=>optical.Contains(s.SurfaceNumber)).ToArray()});
        var detector=channels[channel][4];View.RayFilter=r=>r.Segments.Any(s=>s.TargetSurfaceNumber==detector);View.ShowRays=true;
        scope.Text=$"已隔离 通道 {channel:00} · 光学对象视图";title.Text=$"通道 {channel:00}";
        rayToggle.IsChecked=true;scopeAction.Content="恢复整机";pathNote.Text=$"视图筛选 / 命中 D{channel:00} 的路径 / 最多显示 {View.MaximumDisplayedRays} 条";
        expanded.Add("channel-"+channel);
    }
    public void ShowOverview()
    {
        isolated=0;View.SetScene(originalScene);View.ShowRays=false;View.RayFilter=null;scope.Text="整机视图 · 32 个通道 · 432 个场景对象";title.Text="32 通道照明检测平台";
        rayToggle.IsChecked=false;scopeAction.Content="隔离所选";pathNote.Text="整机范围 / 光线已隐藏 / 选择对象后可隔离所在通道";
    }
    Control BuildCenter()
    {
        var root=new Grid{RowDefinitions=new RowDefinitions("44,38,*,150")};
        var header=new Grid{ColumnDefinitions=new ColumnDefinitions("*,Auto"),Margin=new Thickness(12,4)};
        modelButton.Click+=(_,_)=>{ShowObjectTable(false);tableButton.IsChecked=false;modelButton.IsChecked=true;};tableButton.Click+=(_,_)=>{ShowObjectTable(true);tableButton.IsChecked=true;modelButton.IsChecked=false;};
        header.Children.Add(new StackPanel{Orientation=Orientation.Horizontal,Spacing=6,Children={modelButton,tableButton}});
        rayToggle.IsCheckedChanged+=(_,_)=>{View.ShowRays=rayToggle.IsChecked==true;View.InvalidateVisual();};
        var tools=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8,Children={rayToggle,new ComboBox{ItemsSource=new[]{"按波长","按对象"},SelectedIndex=0,MinWidth=88},new ComboBox{ItemsSource=new[]{"透明","线框"},SelectedIndex=0,MinWidth=80},new ComboBox{ItemsSource=new[]{"等轴测","前视","顶视"},SelectedIndex=0,MinWidth=95}}};Grid.SetColumn(tools,1);header.Children.Add(tools);root.Children.Add(Ui.Surface(header,new Thickness(0,0,0,1)));
        var scopes=new Grid{ColumnDefinitions=new ColumnDefinitions("*,Auto"),Margin=new Thickness(15,3)};scopes.Children.Add(scope);scopeAction.Click+=(_,_)=>{if(isolated==0)IsolateChannel(Math.Clamp((SelectedNumbers.FirstOrDefault(43)-1)/13+1,1,32));else ShowOverview();};Grid.SetColumn(scopeAction,1);scopes.Children.Add(scopeAction);var scopebar=Ui.Surface(scopes,new Thickness(0,0,0,1));Grid.SetRow(scopebar,1);root.Children.Add(scopebar);
        stage.Children.Add(View);stage.Children.Add(new StackPanel{Margin=new Thickness(22),Spacing=6,HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top,Children={title,breadcrumb}});
        pathNote.Margin=new Thickness(18);pathNote.VerticalAlignment=VerticalAlignment.Bottom;pathNote.HorizontalAlignment=HorizontalAlignment.Left;stage.Children.Add(pathNote);
        table.ItemsSource=document.Objects;foreach(var (label,path,width) in new[]{("编号",nameof(NonSequentialObjectRowDto.ObjectNumber),55),("名称",nameof(NonSequentialObjectRowDto.Name),205),("类型",nameof(NonSequentialObjectRowDto.Kind),140),("材料",nameof(NonSequentialObjectRowDto.Material),100),("X (mm)",nameof(NonSequentialObjectRowDto.X),90),("Y (mm)",nameof(NonSequentialObjectRowDto.Y),90),("Z (mm)",nameof(NonSequentialObjectRowDto.Z),90)})
        {
            if(path==nameof(NonSequentialObjectRowDto.Kind))table.Columns.Add(new DataGridTemplateColumn{Header=label,Width=new DataGridLength(width),CellTemplate=new Avalonia.Controls.Templates.FuncDataTemplate<NonSequentialObjectRowDto>((row,_)=>Ui.Text(row?.Kind switch{NonSequentialObjectKind.Null=>"坐标参考",NonSequentialObjectKind.StandardLens=>"标准透镜",NonSequentialObjectKind.SourceEllipse=>"椭圆面光源",NonSequentialObjectKind.DetectorRectangle=>"矩形探测器",NonSequentialObjectKind.Box=>"盒体",_=>row?.Kind.ToString()??""},12))});
            else table.Columns.Add(new DataGridTextColumn{Header=label,Binding=new Binding(path),Width=new DataGridLength(width)});
        }
        table.SelectionChanged+=(_,_)=>{if(table.SelectedItem is NonSequentialObjectRowDto row)SelectNumbers([row.ObjectNumber]);};
        table.IsVisible=false;stageHost.Children.Add(stage);stageHost.Children.Add(table);Grid.SetRow(stageHost,2);root.Children.Add(stageHost);
        var task=TaskPane();Grid.SetRow(task,3);root.Children.Add(task);return root;
    }
    public void ShowObjectTable(bool visible)
    {
        table.IsVisible=visible;stage.IsVisible=!visible;tableButton.IsChecked=visible;modelButton.IsChecked=!visible;scopeAction.IsVisible=!visible;
        scope.Text=visible?"对象表 · 全部 432 个对象":isolated>0?$"已隔离 通道 {isolated:00} · 光学对象视图":"整机视图 · 32 个通道 · 432 个场景对象";
        if(visible&&SelectedNumbers.FirstOrDefault() is var number&&rows.TryGetValue(number,out var row)){table.SelectedItem=row;table.ScrollIntoView(row,null);}
    }
    Control TaskPane()
    {
        var root=new Grid{RowDefinitions=new RowDefinitions("38,*"),Margin=new Thickness(14,0)};
        root.Children.Add(new StackPanel{Orientation=Orientation.Horizontal,Spacing=6,Children={new ToggleButton{Content="任务",IsChecked=true,Padding=new Thickness(15,4)},new Button{Content="光线路径",Padding=new Thickness(15,4)},new Button{Content="探测器",Padding=new Thickness(15,4)}}});
        var table=new Grid{RowDefinitions=new RowDefinitions("28,32,32"),ColumnDefinitions=new ColumnDefinitions("1.4*,1.1*,*,*")};
        void Cell(int row,int col,string text,bool bold=false){var t=Ui.Text(text,12,bold);Grid.SetRow(t,row);Grid.SetColumn(t,col);table.Children.Add(t);}
        foreach(var (s,c) in new[]{("任务",0),("范围",1),("状态",2),("记录",3)})Cell(0,c,s,true);
        Cell(1,0,"布局光线");Cell(1,1,$"{session.SourceObjectIds.Count} 个光源 · 550 nm");Cell(1,2,"已准备");Cell(1,3,$"{session.BranchCount} 条分支");
        Cell(2,0,"照度分析");Cell(2,1,"32 个探测器");Cell(2,2,"未运行");Cell(2,3,"—");
        Grid.SetRow(table,1);root.Children.Add(table);return Ui.Surface(root,new Thickness(0,1,0,0));
    }
    Control ObjectProperties(NonSequentialObjectRowDto[] selected)
    {
        mixed=false;var root=new Grid{RowDefinitions=new RowDefinitions("44,*,56")};var title=Ui.Text(selected.Length==1?"对象属性":"批量属性",15,true);title.Margin=new Thickness(16,0);root.Children.Add(title);
        var body=new StackPanel{Margin=new Thickness(16,9),Spacing=12};var first=selected[0];
        body.Children.Add(Ui.Text(selected.Length==1?first.Name:$"已选择 {selected.Length} 个对象",17,true));
        body.Children.Add(Ui.Text(selected.Length==1?$"结构 / 通道 {(first.ObjectNumber-1)/13+1:00}":string.Join(" · ",selected.Take(3).Select(o=>o.Name.Split(' ')[0])),12));
        bool? CommonBool(Func<NonSequentialObjectRowDto,bool> get)=>selected.All(o=>get(o)==get(first))?get(first):null;
        body.Children.Add(new StackPanel{Orientation=Orientation.Horizontal,Spacing=18,Children={new CheckBox{Content="参与追迹",IsThreeState=true,IsChecked=CommonBool(o=>o.Enabled)},new CheckBox{Content="显示",IsThreeState=true,IsChecked=CommonBool(o=>o.Visible)}}});
        void Section(string label)=>body.Children.Add(new StackPanel{Spacing=8,Margin=new Thickness(0,6,0,0),Children={new Border{Height=1,Background=new SolidColorBrush(Color.FromRgb(224,233,245))},Ui.Text(label,14,true)}});
        void Field(string label,IEnumerable<string> values,string unit="")
        {
            var distinct=values.Distinct().ToArray();var common=distinct.Length==1; if(!common)mixed=true;
            var line=new Grid{ColumnDefinitions=new ColumnDefinitions(unit.Length>0?"112,*,26":"112,*"),ColumnSpacing=8};
            line.Children.Add(selected.Length>1?new CheckBox{Content=label,IsChecked=label=="材料"}:Ui.Text(label));
            var box=new TextBox{Text=common?distinct[0]:"",PlaceholderText=common?"":"不同值",MinHeight=31,Padding=new Thickness(8,4)};Grid.SetColumn(box,1);line.Children.Add(box);if(unit.Length>0){var t=Ui.Text(unit,12);Grid.SetColumn(t,2);line.Children.Add(t);}body.Children.Add(line);
        }
        if(selected.Length==1)Field("名称",selected.Select(o=>o.Name));else body.Children.Add(Ui.Text("勾选需要统一修改的属性",12));
        Field("材料",selected.Select(o=>o.Material));
        if(selected.All(o=>o.Parameters is StandardLensParameters))
        {
            var lenses=selected.Select(o=>(StandardLensParameters)o.Parameters).ToArray();Section("几何");
            Field("前表面半径",lenses.Select(l=>l.FrontRadiusMillimeters.ToString("0.###")),"mm");Field("后表面半径",lenses.Select(l=>l.BackRadiusMillimeters.ToString("0.###")),"mm");Field("中心厚度",lenses.Select(l=>l.CenterThicknessMillimeters.ToString("0.###")),"mm");Field("半口径",lenses.Select(l=>l.SemiDiameterMillimeters.ToString("0.###")),"mm");
        }
        Section("坐标与关系");Field("坐标参考",selected.Select(o=>o.ReferenceObjectId is Guid id?rows.Values.Single(r=>r.Id==id).Name.Split(' ')[0]:"全局坐标系"));
        Field("介质包含",selected.Select(o=>o.ContainingObjectId is null?"无":"有"));
        Field("X",selected.Select(o=>o.X.ToString("0.###")),"mm");Field("Y",selected.Select(o=>o.Y.ToString("0.###")),"mm");Field("Z",selected.Select(o=>o.Z.ToString("0.###")),"mm");
        var scroll=new ScrollViewer{Content=body,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};Grid.SetRow(scroll,1);root.Children.Add(scroll);
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Spacing=8,Margin=new Thickness(15,10),Children={new Button{Content="还原",MinWidth=72},new Button{Content=selected.Length>1?"批量应用":"应用",MinWidth=85}}};Grid.SetRow(buttons,2);root.Children.Add(buttons);return Ui.Surface(root,new Thickness(1,0,0,0));
    }
    Control TaskProperties()
    {
        var body=new StackPanel{Margin=new Thickness(18),Spacing=16,Children={Ui.Text("仿真设置",15,true),Ui.Text("布局光线",18,true),Ui.Text("范围：全部 32 个光源"),Ui.Text("波长：550 nm"),Ui.Text("状态：已准备"),Ui.Text($"光线路径分支：{session.BranchCount}"),Ui.Text($"线段记录：{session.SegmentCount}"),Ui.Text("显示筛选"),new ComboBox{ItemsSource=new[]{"命中 D04","全部路径","包含反射事件"},SelectedIndex=0},new TextBox{Text="80",PlaceholderText="显示条数"},Ui.Text("显示设置独立于追迹设置",12)}};
        return Ui.Surface(new ScrollViewer{Content=body},new Thickness(1,0,0,0));
    }
}
