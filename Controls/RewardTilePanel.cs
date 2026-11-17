using System.Windows;
using System.Windows.Controls;

namespace CloudLightBlizzard.Controls;

/// <summary>Uniform reward tiles sized from available content width, independently of monitor resolution.</summary>
public sealed class RewardTilePanel : Panel
{
    private const double Gap=10, Minimum=172;
    private double _tileWidth=Minimum, _tileHeight;
    private int _columns=1;
    internal static int ColumnsFor(double width)=>Math.Max(1,(int)Math.Floor((Math.Max(0,width)+Gap)/(Minimum+Gap)));
    protected override Size MeasureOverride(Size available)
    {
        var width=double.IsInfinity(available.Width)?Minimum:Math.Max(0,available.Width);
        _columns=ColumnsFor(width);_tileWidth=Math.Max(0,(width-Gap*(_columns-1))/_columns);_tileHeight=0;
        foreach(UIElement child in InternalChildren){child.Measure(new Size(_tileWidth,double.PositiveInfinity));_tileHeight=Math.Max(_tileHeight,child.DesiredSize.Height);}
        var rows=(int)Math.Ceiling(InternalChildren.Count/(double)_columns);
        return new(width,rows==0?0:rows*_tileHeight+(rows-1)*Gap);
    }
    protected override Size ArrangeOverride(Size final)
    {
        for(var index=0;index<InternalChildren.Count;index++)
            InternalChildren[index].Arrange(new Rect(index%_columns*(_tileWidth+Gap),index/_columns*(_tileHeight+Gap),_tileWidth,_tileHeight));
        return final;
    }
}
