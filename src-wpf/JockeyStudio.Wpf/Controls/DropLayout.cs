using System.Collections.Generic;
using System.Windows;
using JockeyStudio.Wpf.Controls;

/// <summary>Pure drop-slot math for the deck-grid reorder drag, split out so the
/// dominant-side rule can be unit-tested. Input rects are the OTHER (remaining)
/// deck cards in reading order; the ghost rect is the dragged card's current box
/// in the same coordinate space.</summary>
public static class DropLayout
{
    /// <summary>Drop slot index (0..rects.Count) in the remaining-deck layout.
    /// Rows fully above the dragged card's vertical center count entirely. Within
    /// its row the slot occupies the CELL that holds a plurality of the card:
    /// the identified cell is the one whose swap region (its width plus half the
    /// inter-cell gap — the visual divider) contains the card's horizontal
    /// center. This makes the indicator sit 1:1 under the dragged card, snap only
    /// when the card's body actually crosses a divider, and behave identically in
    /// every drag direction.</summary>
    public static int ComputeOver(IReadOnlyList<Rect> rects, Rect ghost)
    {
        if (rects.Count == 0) return 0;
        double cx = ghost.Left + ghost.Width / 2.0;
        double cy = ghost.Top + ghost.Height / 2.0;
        int over = rects.Count;
        for (int i = 0; i < rects.Count; i++)
        {
            Rect r = rects[i];
            if (cy >= r.Top + r.Height)
            {
                over = i + 1;
                continue;
            }
            if (cy < r.Top)
            {
                over = i;
                break;
            }
            double flip = NextDivider(rects, i, r);
            if (cx > flip)
            {
                over = i + 1;
                continue;
            }
            over = i;
            break;
        }
        return over;
    }

    /// <summary>Where a plurality of the card has to be before it counts as
    /// being past this cell. Rect list includes the drop slot as an occupied
    /// cell, so <c>over</c> is the exact grid position the slot should land on.
    /// Internal cells flip at the NEXT cell's LEFT EDGE — the point at which
    /// more than half of the dragged card is actually parallel to that next
    /// cell, so a pixel of overlap never trips the indicator. The last cell of a
    /// row flips at its MIDPOINT, because its real next slot is the next row;
    /// keyed to the panel edge it would be unreachable when dragging right.</summary>
    private static double NextDivider(IReadOnlyList<Rect> rects, int i, Rect r)
    {
        for (int j = i + 1; j < rects.Count; j++)
        {
            Rect n = rects[j];
            if (Math.Abs(n.Top - r.Top) < 1.0 && n.Left > r.Right)
            {
                return n.Left;
            }
        }
        return r.Left + r.Width / 2.0;
    }
}