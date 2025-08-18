using Mirror;
using Steamworks;
using UnityEngine;

namespace AutoMapRoom.Wrappers;

public class Player
{
   internal static global::Player GetPlayer() => global::Player._mainPlayer;
}