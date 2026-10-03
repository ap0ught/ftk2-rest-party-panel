using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;

namespace FlockMemory
{
	[BepInPlugin(PluginGuid, "Flock Memory", PluginVersion)]
	public sealed class FlockMemoryPlugin : BaseUnityPlugin
	{
		public const string PluginGuid = "cmayfield.ftk2.flockmemory";
		public const string PluginVersion = "1.0.0";

		internal static FlockMemoryPlugin Instance;

		internal static ConfigEntry<bool> Enabled;
		internal static ConfigEntry<bool> VerboseLogging;
		internal static ConfigEntry<bool> WarnWhenCoop;

		internal bool WarnedThisSession;

		private void Awake()
		{
			Instance = this;

			Enabled = Config.Bind(
				"Flock",
				"Enabled",
				true,
				"Raise a newly herded follower to the flock's high-water mark instead of the run's world level."
			);
			VerboseLogging = Config.Bind(
				"Flock",
				"VerboseLogging",
				false,
				"Log every flock level read and write."
			);
			WarnWhenCoop = Config.Bind(
				"Flock",
				"WarnWhenCoop",
				true,
				"Log a warning the first time an online multiplayer session is seen. Both peers must run this plugin with identical settings or the session will desync."
			);

			try
			{
				Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly());
				Logger.LogInfo($"Flock Memory {PluginVersion} loaded.");
			}
			catch (Exception ex)
			{
				Logger.LogError($"Failed to apply Harmony patches: {ex}");
			}
		}

		internal static void LogInfo(string message)
		{
			if (Instance != null)
			{
				Instance.Logger.LogInfo(message);
			}
		}

		internal static void LogVerbose(string message)
		{
			if (Instance != null && VerboseLogging.Value)
			{
				Instance.Logger.LogInfo(message);
			}
		}

		internal static void LogWarning(string message)
		{
			if (Instance != null)
			{
				Instance.Logger.LogWarning(message);
			}
		}
	}

	/// <summary>
	/// Reads and writes the flock's high-water mark.
	///
	/// The mark is kept in <c>GameRunData.Stats</c>, a plain
	/// <c>Dictionary&lt;string, int&gt;</c> on the run that the game writes to
	/// directly (<c>GameRun.Stats["ENEMIES_KILLED"] = n</c>) and serialises into
	/// the <c>*.ftk2</c> run save. It is deliberately *not* <c>UserData.LocalStats</c>:
	/// that lives in each player's own <c>User.ftk2</c>, so two peers in one
	/// co-op session would read different values and diverge.
	/// </summary>
	internal static class Flock
	{
		internal const string StatKey = "FTK2_FlockLevel";

		/// <summary>
		/// Tag <c>SkillHelper.GetHerdSheep</c> filters on when it picks which
		/// follower the Shepherd's Herd skill summons. Reusing the same test is
		/// what keeps this plugin scoped to sheep: mercs from deeds and every
		/// other follower are left on the stock behaviour.
		/// </summary>
		internal const string HerdTag = "HERD";

		private static GameRunData Run
		{
			get { return RouterHelper.Env != null ? RouterHelper.Env.GameRun : null; }
		}

		internal static bool IsHerdFollower(string configName)
		{
			if (string.IsNullOrEmpty(configName) || Env.Configs == null || Env.Configs.Followers == null)
			{
				return false;
			}

			// SerializedSortedDictionary is enumerated by the game rather than
			// probed, so do the same and stay off its indexer semantics.
			FollowerCharacterConfig config = Env.Configs.Followers
				.FirstOrDefault(pair => pair.Key == configName)
				.Value;
			if (config == null || config.Tags == null)
			{
				return false;
			}
			return config.Tags.Contains(HerdTag);
		}

		internal static int ReadLevel()
		{
			Dictionary<string, int> stats = Run != null ? Run.Stats : null;
			if (stats == null)
			{
				return 0;
			}
			int level;
			return stats.TryGetValue(StatKey, out level) ? level : 0;
		}

		internal static void RaiseTo(int level)
		{
			Dictionary<string, int> stats = Run != null ? Run.Stats : null;
			if (stats == null || level <= 0)
			{
				return;
			}

			int current;
			if (stats.TryGetValue(StatKey, out current) && current >= level)
			{
				FlockMemoryPlugin.LogVerbose($"[flock] level {level} not above mark {current}; unchanged.");
				return;
			}

			stats[StatKey] = level;
			FlockMemoryPlugin.LogInfo($"[flock] high-water mark raised to level {level}.");
		}

		/// <summary>
		/// Returns the level a herded follower should actually spawn at: the
		/// requested one, or the flock's mark when that is higher.
		/// </summary>
		internal static int ApplyFloor(int requested, string configName)
		{
			if (!FlockMemoryPlugin.Enabled.Value || !IsHerdFollower(configName))
			{
				return requested;
			}

			int mark = ReadLevel();
			if (mark <= requested)
			{
				FlockMemoryPlugin.LogVerbose($"[flock] {configName} keeps requested level {requested} (mark {mark}).");
				return requested;
			}

			FlockMemoryPlugin.LogInfo($"[flock] {configName} requested level {requested}, floored to flock mark {mark}.");
			return mark;
		}

		internal static void WarnIfCoop()
		{
			if (!FlockMemoryPlugin.WarnWhenCoop.Value || FlockMemoryPlugin.Instance == null)
			{
				return;
			}
			if (FlockMemoryPlugin.Instance.WarnedThisSession)
			{
				return;
			}

			NetworkData network = RouterHelper.Env != null ? RouterHelper.Env.NetworkData : null;
			if (network == null || !network.PlayingOnlineMultiplayer)
			{
				return;
			}

			FlockMemoryPlugin.Instance.WarnedThisSession = true;
			FlockMemoryPlugin.LogWarning(
				"[flock] online multiplayer session detected (IsHost=" + network.IsHost + "). "
				+ "Flock Memory changes simulation output, so BOTH peers must run it with identical settings "
				+ "or the session will desync."
			);
		}
	}

	/// <summary>
	/// Remembers the flock's high-water mark while a sheep still exists.
	///
	/// <c>FollowerHelper.RemoveFollower</c> is the only place a follower leaves,
	/// and it runs <em>before</em> <c>GameRun.Entities.Remove(pFollower)</c>, so a
	/// prefix is the last moment the departing sheep's level can be read. It
	/// covers death, contract expiry and dismissal alike; the overworld's
	/// <c>_decayEntities</c> sweep runs later and cannot help here.
	/// </summary>
	[HarmonyPatch(typeof(FollowerHelper), nameof(FollowerHelper.RemoveFollower))]
	internal static class RemoveFollowerPatch
	{
		[HarmonyPrefix]
		private static void Prefix(Entity pFollower)
		{
			if (!FlockMemoryPlugin.Enabled.Value || pFollower == null)
			{
				return;
			}

			CharacterComponent character = pFollower.Get<CharacterComponent>();
			if (character == null || !Flock.IsHerdFollower(character.ConfigName))
			{
				return;
			}

			int level = ProgressionHelper.GetEntityLevel(pFollower);
			FlockMemoryPlugin.LogInfo($"[flock] {character.ConfigName} leaving at level {level}; recording it.");
			Flock.RaiseTo(level);
		}
	}

	/// <summary>
	/// Floors the level a herded follower is built at.
	///
	/// <c>SkillHelper.TryProcAndPerformAdventureSkills</c> passes the run's
	/// world level into both <c>CreateFollowerCharacter</c> and
	/// <c>TryProgressCharacterEntityToLevel</c> for the <c>SKILL_HERD</c> case;
	/// both are patched so the entity's base stats and its XP agree.
	/// </summary>
	[HarmonyPatch(typeof(CharacterHelper), nameof(CharacterHelper.CreateFollowerCharacter))]
	internal static class CreateFollowerPatch
	{
		[HarmonyPrefix]
		private static void Prefix(string pFollowerConfig, ref int pLevel)
		{
			Flock.WarnIfCoop();
			pLevel = Flock.ApplyFloor(pLevel, pFollowerConfig);
		}
	}

	[HarmonyPatch(typeof(CharacterHelper), nameof(CharacterHelper.TryProgressCharacterEntityToLevel))]
	internal static class ProgressFollowerPatch
	{
		[HarmonyPrefix]
		private static void Prefix(Entity pCharacterEntity, ref int pLevel)
		{
			if (pCharacterEntity == null)
			{
				return;
			}
			CharacterComponent character = pCharacterEntity.Get<CharacterComponent>();
			if (character == null)
			{
				return;
			}
			pLevel = Flock.ApplyFloor(pLevel, character.ConfigName);
		}
	}
}