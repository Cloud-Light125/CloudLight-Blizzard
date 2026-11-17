using System.Windows;
using System.Windows.Controls;

namespace CloudLightBlizzard.Controls;

/// <summary>Uniform reward tiles sized from available content width, independently of monitor resolution.</summary>
public sealed class RewardTilePanel : Panel
{
    private const double Gap=12, Minimum=190, Maximum=216;
    private double _tileWidth=Minimum, _tileHeight;
    private int _columns=1;
    public static readonly DependencyProperty MinimumTileWidthProperty=DependencyProperty.Register(nameof(MinimumTileWidth),typeof(double),typeof(RewardTilePanel),new FrameworkPropertyMetadata(Minimum,FrameworkPropertyMetadataOptions.AffectsMeasure));
    public static readonly DependencyProperty MaximumTileWidthProperty=DependencyProperty.Register(nameof(MaximumTileWidth),typeof(double),typeof(RewardTilePanel),new FrameworkPropertyMetadata(Maximum,FrameworkPropertyMetadataOptions.AffectsMeasure));
    public static readonly DependencyProperty StretchSparseRowsProperty=DependencyProperty.Register(nameof(StretchSparseRows),typeof(bool),typeof(RewardTilePanel),new FrameworkPropertyMetadata(false,FrameworkPropertyMetadataOptions.AffectsMeasure));
    public bool StretchSparseRows {get=>(bool)GetValue(StretchSparseRowsProperty);set=>SetValue(StretchSparseRowsProperty,value);}
    public double MinimumTileWidth {get=>(double)GetValue(MinimumTileWidthProperty);set=>SetValue(MinimumTileWidthProperty,value);}
    public double MaximumTileWidth {get=>(double)GetValue(MaximumTileWidthProperty);set=>SetValue(MaximumTileWidthProperty,value);}
    internal static int ColumnsFor(double width,double minimum=Minimum)=>Math.Max(1,(int)Math.Floor((Math.Max(0,width)+Gap)/(minimum+Gap)));
    protected override Size MeasureOverride(Size available)
    {
        var width=double.IsInfinity(available.Width)?Minimum:Math.Max(0,available.Width);
        _columns=ColumnsFor(width,MinimumTileWidth);
        if(StretchSparseRows)_columns=Math.Min(_columns,Math.Max(1,InternalChildren.Count));
        _tileWidth=Math.Min(MaximumTileWidth,Math.Max(0,(width-Gap*(_columns-1))/_columns));_tileHeight=0;
        foreach(UIElement child in InternalChildren)child.Measure(new Size(_tileWidth,double.PositiveInfinity));
        var rows=(int)Math.Ceiling(InternalChildren.Count/(double)_columns);
        double height=0;
        for(var row=0;row<rows;row++)height+=RowHeight(row)+(row==0?0:Gap);
        return new(width,height);
    }
    protected override Size ArrangeOverride(Size final)
    {
        double y=0;
        for(var index=0;index<InternalChildren.Count;index++)
        {
            if(index%_columns==0)_tileHeight=RowHeight(index/_columns);
            InternalChildren[index].Arrange(new Rect(index%_columns*(_tileWidth+Gap),y,_tileWidth,_tileHeight));
            if(index%_columns==_columns-1)y+=_tileHeight+Gap;
        }
        return final;
    }
    private double RowHeight(int row)=>InternalChildren.Cast<UIElement>().Skip(row*_columns).Take(_columns).Select(c=>c.DesiredSize.Height).DefaultIfEmpty(0).Max();
}
