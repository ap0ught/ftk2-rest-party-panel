using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine.UIElements;

namespace RestPartyPanel
{
	[BepInPlugin(PluginGuid, "Rest Party Panel", PluginVersion)]
	public sealed class RestPartyPanelPlugin : BaseUnityPlugin
	{
		public const string PluginGuid = "cmayfield.ftk2.restpartypanel";
		public const string PluginVersion = "1.0.0";

		internal static RestPartyPanelPlugin Instance;

		internal static ConfigEntry<bool> FallbackToParty;
		internal static ConfigEntry<bool> KeepDeadVisible;
		internal static ConfigEntry<bool> VerboseLogging;

		private void Awake()
		{
			Instance = this;

			FallbackToParty = Config.Bind(
				"Panel",
				"FallbackToParty",
				true,
				"Combat's left panel lists only non-player allies. When that yields nothing (for example a co-op party of human heroes), fall back to listing the whole party instead of showing an empty panel."
			);
			KeepDeadVisible = Config.Bind(
				"Panel",
				"KeepDeadVisible",
				false,
				"List party members that are down instead of hiding them, so you can see who still needs reviving."
			);
			VerboseLogging = Config.Bind(
				"Panel",
				"VerboseLogging",
				false,
				"Log what gets put into the left panel on every refresh."
			);

			try
			{
				Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly());
				Logger.LogInfo($"Rest Party Panel {PluginVersion} loaded.");
			}
			catch (Exception ex)
			{
				Logger.LogError($"Failed to apply Harmony patches: {ex}");
			}
		}

		internal static void LogInfo(string message)
		{
			Instance?.Logger.LogInfo(message);
		}

		internal static void LogDebug(string message)
		{
			if (Instance != null && VerboseLogging != null && VerboseLogging.Value)
			{
				Instance.Logger.LogInfo(message);
			}
		}
	}

	/// <summary>
	/// The game's own left-hand panel ("combat-detail-holder-left") is only ever populated by
	/// CombatPhase._refreshCombatDetailsLeft. RestPhase never touches it, so it is blank while the
	/// party rests. This fills it with the same entity set combat would use, and only on rest.
	/// </summary>
	internal static class RestPartyPanelRefresher
	{
		private const string LeftHolderName = "combat-detail-holder-left";

		private static bool s_filledByUs;

		[HarmonyPatch(typeof(VenueLayoutViewHelper), nameof(VenueLayoutViewHelper.ShowLayout))]
		internal static class ShowLayoutPatch
		{
			private static void Postfix(eRoutes pSelectedRoute)
			{
				OnShowLayout(pSelectedRoute);
			}
		}

		[HarmonyPatch(typeof(RestPhase), "_refreshUI")]
		internal static class RestPhaseRefreshPatch
		{
			private static void Postfix(RestPhase __instance)
			{
				Fill(__instance);
			}
		}

		internal static void OnShowLayout(eRoutes pSelectedRoute)
		{
			if (pSelectedRoute == eRoutes.REST)
			{
				Fill(null);
				return;
			}

			if (!s_filledByUs)
			{
				return;
			}

			s_filledByUs = false;

			// Combat owns the panel once it is up; it refills it itself.
			if (pSelectedRoute == eRoutes.COMBAT)
			{
				return;
			}

			VenueLayoutViewHelper.LeftCombatDetailHolder?.HideAll();
		}

		private static void Fill(RestPhase pPhase)
		{
			try
			{
				CombatDetailHolderViewHelper holder = VenueLayoutViewHelper.LeftCombatDetailHolder;
				if (holder == null)
				{
					return;
				}

				List<Entity> listed = Collect(pPhase);
				if (listed == null)
				{
					return;
				}

				ForceContainerVisible();

				Entity main = listed.Count > 0 ? listed[0] : null;
				List<Entity> secondaries = listed.Count > 1 ? listed.GetRange(1, listed.Count - 1) : null;
				holder.ShowDetails(main, main, null, secondaries);
				s_filledByUs = true;

				RestPartyPanelPlugin.LogDebug(
					$"Left panel refreshed with {listed.Count} entries: {string.Join(", ", listed.Select(e => SafeName(e)).ToArray())}"
				);
			}
			catch (Exception ex)
			{
				RestPartyPanelPlugin.LogInfo($"Left panel refresh failed: {ex.Message}");
			}
		}

		/// <summary>
		/// Players plus their followers, filtered the way CombatPhase._refreshCombatDetailsLeft
		/// filters: alive, not an enemy, not a player-controlled character, not an inanimate.
		/// </summary>
		private static List<Entity> Collect(RestPhase pPhase)
		{
			Env env = RouterHelper.Env;
			if (env == null || env.GameRun == null)
			{
				return null;
			}

			List<Entity> players = GetPlayerEntities(pPhase) ?? new List<Entity>();
			if (players.Count == 0)
			{
				return null;
			}

			List<Entity> party = CoreHelper.GetPlayersAndFollowers(
				players,
				env.GameRun.PlayerFollowers,
				env.GameRun.Entities
			);

			bool keepDead = RestPartyPanelPlugin.KeepDeadVisible != null && RestPartyPanelPlugin.KeepDeadVisible.Value;

			List<Entity> listed = party.Where(e => MatchesCombatFilter(e, keepDead)).ToList();

			if (listed.Count == 0 && RestPartyPanelPlugin.FallbackToParty != null && RestPartyPanelPlugin.FallbackToParty.Value)
			{
				listed = party.Where(e => IsAliveOrIgnored(e, keepDead)).ToList();
			}

			return listed;
		}

		private static bool MatchesCombatFilter(Entity pEntity, bool pKeepDead)
		{
			if (!pEntity.TryGet<CharacterComponent>(out var character))
			{
				return false;
			}

			if (pEntity.Has<PlayerComponent>())
			{
				return false;
			}

			if (CharacterHelper.IsEnemy(pEntity))
			{
				return false;
			}

			if (character.CharacterType == eCharacterTypes.INANIMATE)
			{
				return false;
			}

			return pKeepDead || IsAliveOrIgnored(pEntity, pKeepDead);
		}

		private static bool IsAliveOrIgnored(Entity pEntity, bool pKeepDead)
		{
			if (pEntity == null || !pEntity.TryGet<CharacterComponent>(out _))
			{
				return false;
			}

			return pKeepDead || !CharacterHelper.IsDead(pEntity);
		}

		private static string SafeName(Entity pEntity)
		{
			try
			{
				return CharacterHelper.GetDisplayName(pEntity);
			}
			catch
			{
				return pEntity.Guid;
			}
		}

		private static List<Entity> GetPlayerEntities(RestPhase pPhase)
		{
			if (pPhase != null)
			{
				object value = Traverse.Create(pPhase).Field("_playerEntities").GetValue();
				if (value is List<Entity> fromPhase)
				{
					return fromPhase;
				}
			}

			// Opened mid-phase (config reload, late patch) - fall back to the live run.
			Env env = RouterHelper.Env;
			return env?.GameRun?.Entities?.FindAll(e => e.Has<PlayerComponent>()) ?? null;
		}

		/// <summary>
		/// The holder sits under the UI root rather than inside a phase layout, but the stylesheet
		/// may still hide it outside combat. Only ever forces it on, never off.
		/// </summary>
		private static void ForceContainerVisible()
		{
			VisualElement root = VenueLayoutViewHelper.GetVenueRoot();
			if (root == null)
			{
				return;
			}

			VisualElement container = root.CachedQ(LeftHolderName);
			if (container == null)
			{
				return;
			}

			try
			{
				if (container.resolvedStyle.display == DisplayStyle.None)
				{
					container.style.display = DisplayStyle.Flex;
					RestPartyPanelPlugin.LogDebug("combat-detail-holder-left was display:none, forced to flex.");
				}
			}
			catch (Exception ex)
			{
				RestPartyPanelPlugin.LogDebug($"Could not inspect holder visibility: {ex.Message}");
			}
		}
	}
}
