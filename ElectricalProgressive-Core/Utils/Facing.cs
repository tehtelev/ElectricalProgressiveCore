﻿using System;
using System.Collections.Generic;
using System.Linq;
using Cairo.Freetype;
using ProtoBuf;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Utils;

[Flags]
[ProtoContract]
public enum Facing {
    None = 0b_0000_0000_0000_0000_0000_0000,

    AllAll = 0b_1111_1111_1111_1111_1111_1111,
    AllNorth = Facing.EastNorth | Facing.WestNorth | Facing.UpNorth | Facing.DownNorth,
    AllEast = Facing.NorthEast | Facing.SouthEast | Facing.UpEast | Facing.DownEast,
    AllSouth = Facing.EastSouth | Facing.WestSouth | Facing.UpSouth | Facing.DownSouth,
    AllWest = Facing.NorthWest | Facing.SouthWest | Facing.UpWest | Facing.DownWest,
    AllUp = Facing.NorthUp | Facing.EastUp | Facing.SouthUp | Facing.WestUp,
    AllDown = Facing.NorthDown | Facing.EastDown | Facing.SouthDown | Facing.WestDown,

    NorthAll = Facing.NorthEast | Facing.NorthWest | Facing.NorthUp | Facing.NorthDown,
    NorthEast = 0b_1000_0000_0000_0000_0000_0000,
    NorthWest = 0b_0100_0000_0000_0000_0000_0000,
    NorthUp = 0b_0010_0000_0000_0000_0000_0000,
    NorthDown = 0b_0001_0000_0000_0000_0000_0000,

    EastAll = Facing.EastNorth | Facing.EastSouth | Facing.EastUp | Facing.EastDown,
    EastNorth = 0b_0000_1000_0000_0000_0000_0000,
    EastSouth = 0b_0000_0100_0000_0000_0000_0000,
    EastUp = 0b_0000_0010_0000_0000_0000_0000,
    EastDown = 0b_0000_0001_0000_0000_0000_0000,

    SouthAll = Facing.SouthEast | Facing.SouthWest | Facing.SouthUp | Facing.SouthDown,
    SouthEast = 0b_0000_0000_1000_0000_0000_0000,
    SouthWest = 0b_0000_0000_0100_0000_0000_0000,
    SouthUp = 0b_0000_0000_0010_0000_0000_0000,
    SouthDown = 0b_0000_0000_0001_0000_0000_0000,

    WestAll = Facing.WestNorth | Facing.WestSouth | Facing.WestUp | Facing.WestDown,
    WestNorth = 0b_0000_0000_0000_1000_0000_0000,
    WestSouth = 0b_0000_0000_0000_0100_0000_0000,
    WestUp = 0b_0000_0000_0000_0010_0000_0000,
    WestDown = 0b_0000_0000_0000_0001_0000_0000,

    UpAll = Facing.UpNorth | Facing.UpEast | Facing.UpSouth | Facing.UpWest,
    UpNorth = 0b_0000_0000_0000_0000_1000_0000,
    UpEast = 0b_0000_0000_0000_0000_0100_0000,
    UpSouth = 0b_0000_0000_0000_0000_0010_0000,
    UpWest = 0b_0000_0000_0000_0000_0001_0000,

    DownAll = Facing.DownNorth | Facing.DownEast | Facing.DownSouth | Facing.DownWest,
    DownNorth = 0b_0000_0000_0000_0000_0000_1000,
    DownEast = 0b_0000_0000_0000_0000_0000_0100,
    DownSouth = 0b_0000_0000_0000_0000_0000_0010,
    DownWest = 0b_0000_0000_0000_0000_0000_0001
}

public static class FacingHelper
{
    private static readonly BlockFacing[] blockfaces = {
        BlockFacing.NORTH,
        BlockFacing.EAST,
        BlockFacing.SOUTH,
        BlockFacing.WEST,
        BlockFacing.UP,
        BlockFacing.DOWN
    };




    public static Facing FromFace(BlockFacing face)
    {
        if (face == null)
            return Facing.None;
        return face.Index switch
        {
            BlockFacing.indexNORTH => Facing.NorthAll,
            BlockFacing.indexEAST => Facing.EastAll,
            BlockFacing.indexSOUTH => Facing.SouthAll,
            BlockFacing.indexWEST => Facing.WestAll,
            BlockFacing.indexUP => Facing.UpAll,
            BlockFacing.indexDOWN => Facing.DownAll,
            _ => Facing.None
        };
    }

    public static Facing FromDirection(BlockFacing direction)
    {
        if (direction == null)
            return Facing.None;

        return direction.Index switch
        {
            BlockFacing.indexNORTH => Facing.AllNorth,
            BlockFacing.indexEAST => Facing.AllEast,
            BlockFacing.indexSOUTH => Facing.AllSouth,
            BlockFacing.indexWEST => Facing.AllWest,
            BlockFacing.indexUP => Facing.AllUp,
            BlockFacing.indexDOWN => Facing.AllDown,
            _ => Facing.None
        };
    }


    /// <summary>
    /// Выдает по индексу соответствующий BlockFacing
    /// </summary>
    /// <param name="flag"></param>
    /// <returns></returns>
    public static BlockFacing BlockFacingFromIndex(int flag)
    {
        return (flag >= 0 && flag < blockfaces.Length) ? blockfaces[flag] : null!;
    }




    public static Facing From(BlockFacing face, BlockFacing direction)
    {
        return FromFace(face) & FromDirection(direction);
    }





    /// <summary>
    /// Выдает все направления, которые соответствуют флагам Face.
    /// </summary>
    /// <param name="self"></param>
    /// <returns></returns>
    public static IEnumerable<BlockFacing> Faces(Facing self)
    {
        var result = new List<BlockFacing>(6);

        if ((self & Facing.NorthAll) != 0)
            result.Add(BlockFacing.NORTH);
        if ((self & Facing.EastAll) != 0)
            result.Add(BlockFacing.EAST);
        if ((self & Facing.SouthAll) != 0)
            result.Add(BlockFacing.SOUTH);
        if ((self & Facing.WestAll) != 0)
            result.Add(BlockFacing.WEST);
        if ((self & Facing.UpAll) != 0)
            result.Add(BlockFacing.UP);
        if ((self & Facing.DownAll) != 0)
            result.Add(BlockFacing.DOWN);

        return result;
    }


    /// <summary>
    /// Выдает все направления, которые соответствуют флагам Directions.
    /// </summary>
    /// <param name="self"></param>
    /// <returns></returns>
    public static IEnumerable<BlockFacing> Directions(Facing self)
    {
        var result = new List<BlockFacing>(6);

        if ((self & Facing.AllNorth) != 0)
            result.Add(BlockFacing.NORTH);
        if ((self & Facing.AllEast) != 0)
            result.Add(BlockFacing.EAST);
        if ((self & Facing.AllSouth) != 0)
            result.Add(BlockFacing.SOUTH);
        if ((self & Facing.AllWest) != 0)
            result.Add(BlockFacing.WEST);
        if ((self & Facing.AllUp) != 0)
            result.Add(BlockFacing.UP);
        if ((self & Facing.AllDown) != 0)
            result.Add(BlockFacing.DOWN);

        return result;
    }

    




    public static Facing FullFace(Facing self)
    {
        return Faces(self).Aggregate(Facing.None, (current, face) => current | FromFace(face));
    }



    public static int Count(Facing self)
    {
        var count = 0;

        while (self != Facing.None)
        {
            count++;
            self &= self - 1;
        }

        return count;
    }



    public static int GetFaceIndex(Facing facing)
    {
        if (facing == Facing.None) return -1;

        if ((facing & Facing.NorthEast) != 0) return 0;
        if ((facing & Facing.NorthWest) != 0) return 0;
        if ((facing & Facing.NorthUp) != 0) return 0;
        if ((facing & Facing.NorthDown) != 0) return 0;

        if ((facing & Facing.EastNorth) != 0) return 1;
        if ((facing & Facing.EastSouth) != 0) return 1;
        if ((facing & Facing.EastUp) != 0) return 1;
        if ((facing & Facing.EastDown) != 0) return 1;

        if ((facing & Facing.SouthEast) != 0) return 2;
        if ((facing & Facing.SouthWest) != 0) return 2;
        if ((facing & Facing.SouthUp) != 0) return 2;
        if ((facing & Facing.SouthDown) != 0) return 2;

        if ((facing & Facing.WestNorth) != 0) return 3;
        if ((facing & Facing.WestSouth) != 0) return 3;
        if ((facing & Facing.WestUp) != 0) return 3;
        if ((facing & Facing.WestDown) != 0) return 3;

        if ((facing & Facing.UpNorth) != 0) return 4;
        if ((facing & Facing.UpEast) != 0) return 4;
        if ((facing & Facing.UpSouth) != 0) return 4;
        if ((facing & Facing.UpWest) != 0) return 4;

        if ((facing & Facing.DownNorth) != 0) return 5;
        if ((facing & Facing.DownEast) != 0) return 5;
        if ((facing & Facing.DownSouth) != 0) return 5;
        if ((facing & Facing.DownWest) != 0) return 5;

        return -1;
    }




    /*

//вычисляет противоположные направления
public static Facing OppositeDirection(Facing self)
{
    var result = Facing.None;

    result |= (self & Facing.NorthEast) != 0
        ? Facing.NorthWest
        : Facing.None;

    result |= (self & Facing.NorthWest) != 0
        ? Facing.NorthEast
        : Facing.None;

    result |= (self & Facing.NorthUp) != 0
        ? Facing.NorthDown
        : Facing.None;

    result |= (self & Facing.NorthDown) != 0
        ? Facing.NorthUp
        : Facing.None;

    result |= (self & Facing.EastNorth) != 0
        ? Facing.EastSouth
        : Facing.None;

    result |= (self & Facing.EastSouth) != 0
        ? Facing.EastNorth
        : Facing.None;

    result |= (self & Facing.EastUp) != 0
        ? Facing.EastDown
        : Facing.None;

    result |= (self & Facing.EastDown) != 0
        ? Facing.EastUp
        : Facing.None;

    result |= (self & Facing.SouthEast) != 0
        ? Facing.SouthWest
        : Facing.None;

    result |= (self & Facing.SouthWest) != 0
        ? Facing.SouthEast
        : Facing.None;

    result |= (self & Facing.SouthUp) != 0
        ? Facing.SouthDown
        : Facing.None;

    result |= (self & Facing.SouthDown) != 0
        ? Facing.SouthUp
        : Facing.None;

    result |= (self & Facing.UpNorth) != 0
        ? Facing.UpSouth
        : Facing.None;

    result |= (self & Facing.UpEast) != 0
        ? Facing.UpWest
        : Facing.None;

    result |= (self & Facing.UpSouth) != 0
        ? Facing.UpNorth
        : Facing.None;

    result |= (self & Facing.UpWest) != 0
        ? Facing.UpEast
        : Facing.None;

    result |= (self & Facing.DownNorth) != 0
        ? Facing.DownSouth
        : Facing.None;

    result |= (self & Facing.DownEast) != 0
        ? Facing.DownWest
        : Facing.None;

    result |= (self & Facing.DownSouth) != 0
        ? Facing.DownNorth
        : Facing.None;

    result |= (self & Facing.DownWest) != 0
        ? Facing.DownEast
        : Facing.None;


    result |= (self & Facing.WestNorth) != 0
        ? Facing.WestSouth
        : Facing.None;

    result |= (self & Facing.WestSouth) != 0
        ? Facing.WestNorth
        : Facing.None;

    result |= (self & Facing.WestUp) != 0
        ? Facing.WestDown
        : Facing.None;

    result |= (self & Facing.WestDown) != 0
        ? Facing.WestUp
        : Facing.None;


    return result;
}

*/

}
