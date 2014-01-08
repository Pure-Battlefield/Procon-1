/**
Copyright (c) 2013, Roi Atalla
All rights reserved.

Redistribution and use in source and binary forms, with or without modification,
are permitted provided that the following conditions are met:

  Redistributions of source code must retain the above copyright notice, this
  list of conditions and the following disclaimer.

  Redistributions in binary form must reproduce the above copyright notice, this
  list of conditions and the following disclaimer in the documentation and/or
  other materials provided with the distribution.

  Neither the name of the {organization} nor the names of its
  contributors may be used to endorse or promote products derived from
  this software without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE FOR
ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
(INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON
ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
*/
using System;
using System.IO;
using System.Text;
using System.Reflection;
using System.Collections.Generic;
using System.Data;
using System.Text.RegularExpressions;
using System.Threading;

using PRoCon.Core;
using PRoCon.Core.Plugin;
using PRoCon.Core.Plugin.Commands;
using PRoCon.Core.Players;

namespace PRoConEvents
{
	public class PromotePlugin : PRoConPluginAPI, IPRoConPluginInterface
	{
		private bool pluginEnabled = false;

		private enum BoolName
		{
			DEBUG_MODE
		}

		private enum MessageName
		{
			USAGE_INSTRUCTIONS,
			NO_SQUAD_LIST_QUERY,
			PLAYER_NOT_IN_YOUR_SQUAD,
			PLAYER_DOES_NOT_EXIST,
			INVALID_INDEX,
			CANCELING_OLD_PROMOTION,
			PLAYER_PROMOTED,
			PLAYER_ACCEPTED_PROMOTION,
			PLAYER_DECLINED_PROMOTION,
			PLAYER_LEFT_DURING_PROMOTION,

			NOT_IN_A_SQUAD,
			NOT_SQUAD_LEADER,
			NOT_BEING_PROMOTED,
			YOUR_PROMOTION_CANCELED,
			YOUR_PROMOTION_CANCELED_SQUAD_LEADER_LEFT,
			YOU_HAVE_BEEN_PROMOTED,
			YOU_HAVE_ACCEPTED_PROMOTION,
			YOU_HAVE_DECLINED_PROMOTION,
		}

		private class Variable<T>
		{
			private string description;
			private T value;

			public string Description
			{
				get { return description; }
				private set { description = value; }
			}
			public T Value
			{
				get { return value; }
				set { this.value = value; }
			}

			public Variable(string description, T value)
			{
				Description = description;
				Value = value;
			}
		}

		private class SquadRecord
		{
			private string squadLeader;
			private List<string> squadMembers;

			private int teamID, squadID;

			public SquadRecord(string squadLeader, int teamID, int squadID)
			{
				this.squadLeader = squadLeader;
				this.teamID = teamID;
				this.squadID = squadID;

				squadMembers = new List<string>();
			}

			public void populateMembers(Dictionary<string, CPlayerInfo> allPlayers)
			{
				squadMembers.Clear();

				foreach (string player in allPlayers.Keys)
				{
					if (player.Equals(squadLeader))
						continue;

					CPlayerInfo playerInfo = allPlayers[player];

					if (playerInfo.TeamID == teamID && playerInfo.SquadID == squadID)
						squadMembers.Add(player);
				}
			}

			public int getTeamID()
			{
				return teamID;
			}

			public int getSquadID()
			{
				return squadID;
			}

			public string getSquadLeader()
			{
				return squadLeader;
			}

			public List<string> getSquadMembers()
			{
				return squadMembers;
			}
		}

		private Dictionary<string, SquadRecord> squadRecords;
		private Dictionary<string, string> promotedPlayers; //player being promoted, squad leader
		
		private Dictionary<BoolName, Variable<bool>> bools;
		private Dictionary<MessageName, Variable<string>> messages;

		public PromotePlugin()
		{
			bools = new Dictionary<BoolName, Variable<bool>>();
			bools.Add(BoolName.DEBUG_MODE, new Variable<bool>("Variables|Debug mode", false));

			messages = new Dictionary<MessageName,Variable<string>>();
			messages.Add(MessageName.USAGE_INSTRUCTIONS, new Variable<string>("Squad Leader Messages|Usage Instructions (following line is numbered list)", "Usage: '/promote N' where N is player number:"));
			messages.Add(MessageName.NO_SQUAD_LIST_QUERY, new Variable<string>("Squad Leader Messages|No Squad List Query", "You haven't queried a numbered list of your squad members. Type '/promote' first."));
			messages.Add(MessageName.PLAYER_NOT_IN_YOUR_SQUAD, new Variable<string>("Squad Leader Messages|Player Not in Your Squad ({0} = player name)", "This player is not in your squad."));
			messages.Add(MessageName.PLAYER_DOES_NOT_EXIST, new Variable<string>("Squad Leader Messages|Player Does Not Exist ({0} = player name)", "This player does not exist on this server."));
			messages.Add(MessageName.INVALID_INDEX, new Variable<string>("Squad Leader Messages|Invalid Index", "Invalid player index."));
			messages.Add(MessageName.CANCELING_OLD_PROMOTION, new Variable<string>("Squad Leader Messages|Canceling Previous Promotion ({0} = promoted player name)", "Your promotion of {0} has been canceled."));
			messages.Add(MessageName.PLAYER_PROMOTED, new Variable<string>("Squad Leader Messages|Player Promoted ({0} = player name)", "{0} has been notified of the promotion."));
			messages.Add(MessageName.PLAYER_ACCEPTED_PROMOTION, new Variable<string>("Squad Leader Messages|Player Accepted Promotion ({0} = player name)", "{0} has accepted the promotion and has been made squad leader."));
			messages.Add(MessageName.PLAYER_DECLINED_PROMOTION, new Variable<string>("Squad Leader Messages|Player Declined Promotion ({0} = player name)", "{0} has declined the promotion."));
			messages.Add(MessageName.PLAYER_LEFT_DURING_PROMOTION, new Variable<string>("Squad Leader Messages|Player Left During Promotions ({0} = player name, {1} = reason: squad, team, server)", "{0} has left the {1}, promotion canceled."));

			messages.Add(MessageName.NOT_IN_A_SQUAD, new Variable<string>("Player Messages|Not In a Squad", "You're not in a squad!"));
			messages.Add(MessageName.NOT_SQUAD_LEADER, new Variable<string>("Player Messages|Not Squad Leader", "You are not the squad leader!"));
			messages.Add(MessageName.NOT_BEING_PROMOTED, new Variable<string>("Player Messages|Not Being Promoted", "You are not being promoted!"));
			messages.Add(MessageName.YOUR_PROMOTION_CANCELED, new Variable<string>("Player Messages|Your Promotion Canceled ({0} = squad leader name)", "Your promotion has been canceled by {0}."));
			messages.Add(MessageName.YOUR_PROMOTION_CANCELED_SQUAD_LEADER_LEFT, new Variable<string>("Player Messages|Your Promotion Canceled Because Squad Leader Left ({0} = squad leader name, {1} = reason: squad, team, server)", "Your promotion has been canceled because the squad leader, {0}, has left the {1}."));
			messages.Add(MessageName.YOU_HAVE_BEEN_PROMOTED, new Variable<string>("Player Messages|You Have Been Promoted ({0} = squad leader name)", "You have been promoted by your squad leader {0}!\nType '/accept' or '/decline' to accept/decline the promotion before end-of-round."));
			messages.Add(MessageName.YOU_HAVE_ACCEPTED_PROMOTION, new Variable<string>("Player Messages|You Have Accepted Promotion", "You have accepted the promotion and have been made squad leader."));
			messages.Add(MessageName.YOU_HAVE_DECLINED_PROMOTION, new Variable<string>("Player Messages|You Have Declined Promotion", "You have declined the promotion."));

			squadRecords = new Dictionary<string, SquadRecord>();
			promotedPlayers = new Dictionary<string, string>();
		}

		public string GetPluginName()
		{
			return "Promote Plugin";
		}

		public string GetPluginVersion()
		{
			return "1.0.0";
		}

		public string GetPluginAuthor()
		{
			return "ra4king";
		}

		public string GetPluginWebsite()
		{
			return "purebattlefield.org";
		}

		public string GetPluginDescription()
		{
			return @"Commands: <br/>
<table>
<tr><td>/promote</td><td>Prints numbered list of squad members</td></tr>
<tr><td>/promote N</td><td>N = number or name. To use a number, command must be called without parameters first</td></tr>
<tr><td>/accept</td><td>Accepts promotion to squad leader</td></tr>
<tr><td>/decline</td><td>Declines promotion to squad leader</td</tr>
</table>";
		}

		public void OnPluginLoaded(string strHostName, string strPort, string strPRoConVersion)
		{
			this.RegisterEvents(this.GetType().Name, "OnGlobalChat", "OnSquadLeader", "OnTeamChat", "OnSquadChat", "OnRoundOver", "OnRestartLevel", "OnRunNextLevel", "OnEndRound", "OnListPlayers", "OnPlayerJoin", "OnPlayerLeft", "OnPlayerMovedByAdmin", "OnPlayerTeamChange", "OnPlayerSquadChange");
		}
		
		public void OnPluginEnable()
		{
			this.pluginEnabled = true;

			ConsoleWrite("^2" + GetPluginName() + " Enabled");
		}

		public void OnPluginDisable()
		{
			this.pluginEnabled = false;

			ConsoleWrite("^8" + GetPluginName() + " Disabled");
		}

		public List<CPluginVariable> GetDisplayPluginVariables()
		{
			List<CPluginVariable> pluginVariables = new List<CPluginVariable>();

			foreach (BoolName name in bools.Keys)
				pluginVariables.Add(new CPluginVariable(bools[name].Description, "bool", string.Concat(bools[name].Value)));
			foreach (MessageName name in messages.Keys)
				pluginVariables.Add(new CPluginVariable(messages[name].Description, "multiline", messages[name].Value));

			return pluginVariables;
		}

		public List<CPluginVariable> GetPluginVariables()
		{
			return GetDisplayPluginVariables();
		}

		public void SetPluginVariable(string strVariable, string strValue)
		{
			foreach (BoolName name in bools.Keys)
			{
				Variable<bool> v = bools[name];

				if (v.Description.Contains(strVariable))
				{
					try
					{
						v.Value = bool.Parse(strValue);
					}
					catch
					{
						ConsoleException("Invalid value for " + name + ": " + strValue);
						return;
					}

					ConsoleWrite(name + " value changed to " + v.Value + ".");

					return;
				}
			}

			foreach (MessageName name in messages.Keys)
			{
				Variable<string> v = messages[name];

				if (v.Description.Contains(strVariable))
				{
					v.Value = strValue;

					ConsoleWrite(name + " message modified.");

					return;
				}
			}

			ConsoleError("Invalid variable: " + strVariable + " with value: " + strValue);
		}

		public override void OnSquadChat(string speaker, string message, int teamId, int squadId)
		{
			OnGlobalChat(speaker, message);
		}

		public override void OnTeamChat(string speaker, string message, int teamId)
		{
			OnGlobalChat(speaker, message);
		}

		private Dictionary<string, string> speakerIntent = new Dictionary<string, string>(); //speaker, intent

		public override void OnGlobalChat(string speaker, string message)
		{
			if (!pluginEnabled)
				return;

			if (speaker.ToLower().Equals("server"))
				return;

			message = message.Trim();

			if (!message.StartsWith("/") && message.StartsWith("!"))
				return;

			message = message.Substring(1);

			if (message.Equals("promote", StringComparison.OrdinalIgnoreCase))
			{
				ConsoleDebug(speaker + " typed " + message + ".");

				CPlayerInfo playerInfo = base.FrostbitePlayerInfoList[speaker];

				if (playerInfo == null)
				{
					AdminSayPlayer("Apparently, you don't exist.", speaker);
					return;
				}

				speakerIntent.Add(speaker, "");

				this.ExecuteCommand("procon.protected.send", "squad.leader", playerInfo.TeamID.ToString(), playerInfo.SquadID.ToString());
			}
			else if (message.StartsWith("promote ", StringComparison.OrdinalIgnoreCase)) //the space at the end is intentional
			{
				ConsoleDebug(speaker + " typed " + message + ".");

				CPlayerInfo playerInfo = base.FrostbitePlayerInfoList[speaker];

				if (playerInfo.SquadID == 0)
				{
					ConsoleDebug("Player is not in a squad.");

					AdminSayPlayer(messages[MessageName.NOT_IN_A_SQUAD].Value, speaker);
					return;
				}

				string intent = message.Substring(8);

				if (intent.Equals(""))
				{
					ConsoleDebug(speaker + " specified empty intent.");

					AdminSayPlayer("Invalid command.", speaker);
					return;
				}

				speakerIntent.Add(speaker, intent);

				if (playerInfo == null)
				{
					AdminSayPlayer("Apparently, you don't exist.", speaker);
					return;
				}

				this.ExecuteCommand("procon.protected.send", "squad.leader", playerInfo.TeamID.ToString(), playerInfo.SquadID.ToString());
			}
			else if (message.Equals("accept", StringComparison.OrdinalIgnoreCase))
			{
				ConsoleDebug(speaker + " typed " + message + ".");

				if (!promotedPlayers.ContainsKey(speaker))
				{
					ConsoleDebug(speaker + " is not being promoted.");

					AdminSayPlayer(messages[MessageName.NOT_BEING_PROMOTED].Value, speaker);
					return;
				}

				if (!base.FrostbitePlayerInfoList.ContainsKey(promotedPlayers[speaker]))
				{
					ConsoleDebug(speaker + " cannot be promoted as old squad leader has left.");

					AdminSayPlayer(String.Format(messages[MessageName.YOUR_PROMOTION_CANCELED_SQUAD_LEADER_LEFT].Value, promotedPlayers[speaker], "server"), speaker);
					return;
				}

				CPlayerInfo promotedPlayerInfo = base.FrostbitePlayerInfoList[speaker];

				CPlayerInfo squadLeaderPlayerInfo = base.FrostbitePlayerInfoList[promotedPlayers[speaker]];

				if (promotedPlayerInfo.TeamID != squadLeaderPlayerInfo.TeamID || promotedPlayerInfo.SquadID != squadLeaderPlayerInfo.SquadID)
				{
					ConsoleDebug(speaker + " has switched squads. Canceling promotion.");

					AdminSayPlayer(String.Format(messages[MessageName.YOUR_PROMOTION_CANCELED_SQUAD_LEADER_LEFT].Value, promotedPlayers[speaker], "squad"), speaker);
					return;
				}

				this.ExecuteCommand("procon.protected.send", "squad.leader", squadLeaderPlayerInfo.TeamID.ToString(), squadLeaderPlayerInfo.SquadID.ToString(), speaker);

				AdminSayPlayer(String.Format(messages[MessageName.PLAYER_ACCEPTED_PROMOTION].Value, speaker), squadLeaderPlayerInfo.SoldierName);
				AdminSayPlayer(messages[MessageName.YOU_HAVE_ACCEPTED_PROMOTION].Value, speaker);

				squadRecords.Remove(squadLeaderPlayerInfo.SoldierName);
				promotedPlayers.Remove(speaker);

				ConsoleDebug(speaker + " has been promoted to squad leader by " + squadLeaderPlayerInfo.SoldierName + ".");
			}
			else if (message.Equals("decline", StringComparison.OrdinalIgnoreCase))
			{
				ConsoleDebug(speaker + " typed " + message + ".");

				if (!promotedPlayers.ContainsKey(speaker))
				{
					ConsoleDebug(speaker + " is not being promoted.");

					AdminSayPlayer(messages[MessageName.NOT_BEING_PROMOTED].Value, speaker);
					return;
				}

				CPlayerInfo squadLeaderPlayerInfo = base.FrostbitePlayerInfoList[promotedPlayers[speaker]];

				AdminSayPlayer(String.Format(messages[MessageName.PLAYER_DECLINED_PROMOTION].Value, speaker), squadLeaderPlayerInfo.SoldierName);
				AdminSayPlayer(messages[MessageName.YOU_HAVE_DECLINED_PROMOTION].Value, speaker);

				squadRecords.Remove(squadLeaderPlayerInfo.SoldierName);
				promotedPlayers.Remove(speaker);

				ConsoleDebug(speaker + " has declined the promotion by " + squadLeaderPlayerInfo.SoldierName + ".");
			}
		}

		public override void OnSquadLeader(int teamId, int squadId, string soldierName)
		{
			ConsoleDebug(soldierName + " is the squad leader of team " + teamId + " squad " + squadId);

			try
			{
				string speaker = null;
				foreach (string s in speakerIntent.Keys)
				{
					if (!base.FrostbitePlayerInfoList.ContainsKey(s))
						continue;

					CPlayerInfo sPlayerInfo = base.FrostbitePlayerInfoList[s];
					if (sPlayerInfo.TeamID == teamId && sPlayerInfo.SquadID == squadId)
					{
						speaker = s;
						break;
					}
				}

				if (speaker == null)
					return;

				string intent = speakerIntent[speaker];

				speakerIntent.Remove(speaker);

				if (!speaker.Equals(soldierName))
				{
					ConsoleDebug(speaker + " is not a squad leader.");

					AdminSayPlayer(messages[MessageName.NOT_SQUAD_LEADER].Value, speaker);
					return;
				}

				SquadRecord squadRecord = squadRecords.ContainsKey(soldierName) ? squadRecords[soldierName] : null;

				ConsoleDebug("A SquadRecord " + (squadRecord == null ? "exists" : "does not exist") + ".");

				if (intent.Equals(""))
				{
					if (squadRecord == null)
					{
						squadRecord = new SquadRecord(soldierName, teamId, squadId);
						squadRecords.Add(soldierName, squadRecord);
					}

					squadRecord.populateMembers(base.FrostbitePlayerInfoList);

					AdminSayPlayer(messages[MessageName.USAGE_INSTRUCTIONS].Value, soldierName);

					string playerList = "";
					if (squadRecord.getSquadMembers().Count == 0)
						playerList = "Empty squad.";
					else
					{
						int count = 0;
						foreach (string member in squadRecord.getSquadMembers())
						{
							playerList += (++count) + ": " + member + ", ";
						}

						playerList = playerList.Substring(0, playerList.Length - 2);
					}

					AdminSayPlayer(playerList, soldierName);

					ConsoleDebug(speaker + " has received usage instructions and numbered squad list.");
				}
				else
				{
					ConsoleDebug(soldierName + " intended " + intent + ".");

					int memberIndex = 0;
					
					if (int.TryParse(intent, out memberIndex) && squadRecord == null)
					{
						ConsoleDebug(speaker + " has not queried a number squad list.");

						AdminSayPlayer(messages[MessageName.NO_SQUAD_LIST_QUERY].Value, soldierName);
						return;
					}

					CPlayerInfo intentPlayerInfo;

					if (memberIndex == 0 && !intent.Equals("0"))
					{
						intentPlayerInfo = base.FrostbitePlayerInfoList.ContainsKey(intent) ? base.FrostbitePlayerInfoList[intent] : null;
					}
					else
					{
						if (memberIndex <= 0 || memberIndex > squadRecord.getSquadMembers().Count)
						{
							ConsoleDebug(speaker + " has specified an invalid index.");

							AdminSayPlayer(messages[MessageName.INVALID_INDEX].Value, soldierName);
							return;
						}

						string intentPlayerName = squadRecord.getSquadMembers()[memberIndex - 1];
						intentPlayerInfo = base.FrostbitePlayerInfoList.ContainsKey(intentPlayerName) ? base.FrostbitePlayerInfoList[intentPlayerName] : null;
					}

					if (intentPlayerInfo == null)
					{
						ConsoleDebug(speaker + " has specified a non-existant player.");

						AdminSayPlayer(String.Format(messages[MessageName.PLAYER_DOES_NOT_EXIST].Value, intent), soldierName);
						return;
					}

					if (intentPlayerInfo.TeamID != teamId || intentPlayerInfo.SquadID != squadId)
					{
						ConsoleDebug(speaker + " has specificed a player not in the same squad.");

						AdminSayPlayer(String.Format(messages[MessageName.PLAYER_NOT_IN_YOUR_SQUAD].Value, intent), soldierName);
						return;
					}

					if (promotedPlayers.ContainsValue(soldierName))
					{
						string oldPromotedPlayer = null;

						foreach (string s in promotedPlayers.Keys)
							if (promotedPlayers[s].Equals(soldierName))
							{
								oldPromotedPlayer = s;
								break;
							}

						if (oldPromotedPlayer == null)
						{
							ConsoleError("wat1");
							return;
						}

						promotedPlayers.Remove(oldPromotedPlayer);

						AdminSayPlayer(String.Format(messages[MessageName.CANCELING_OLD_PROMOTION].Value, oldPromotedPlayer), soldierName);
						AdminSayPlayer(String.Format(messages[MessageName.YOUR_PROMOTION_CANCELED].Value, soldierName), oldPromotedPlayer);

						ConsoleDebug(speaker + " has canceled older promotion of " + oldPromotedPlayer + ".");
					}

					promotedPlayers.Add(intentPlayerInfo.SoldierName, soldierName);

					AdminSayPlayer(String.Format(messages[MessageName.PLAYER_PROMOTED].Value, intentPlayerInfo.SoldierName), soldierName);
					AdminSayPlayer(String.Format(messages[MessageName.YOU_HAVE_BEEN_PROMOTED].Value, soldierName), intentPlayerInfo.SoldierName);

					ConsoleDebug(speaker + " has promoted " + intentPlayerInfo.SoldierName + ".");
				}
			}
			catch(Exception e)
			{
				ConsoleError("ERROR!! " + e.Message);
				ConsoleError(e.StackTrace);
			}
		}

		private string formatTime(int seconds)
		{
			if (seconds < 0)
			{
				ConsoleError("formatTime: SECONDS IS NEGATIVE: " + seconds);
				return "";
			}

			int minutes = seconds / 60;
			seconds %= 60;
			return (minutes > 0 ? minutes + " minute" + (minutes == 0 || minutes > 1 ? "s" : "") : "") + (seconds > 0 || minutes == 0 ? (minutes > 0 ? " and " : "") + seconds + " second" + (seconds == 0 || seconds > 1 ? "s" : "") : "");
		}

		public override void OnEndRound(int iWinningTeamID)
		{
			OnRoundOver(iWinningTeamID);
		}

		public override void OnRunNextLevel()
		{
			OnRoundOver(0);
		}

		public override void OnRestartLevel()
		{
			OnRoundOver(0);
		}

		public override void OnRoundOver(int iWinningTeamID)
		{
			speakerIntent.Clear();
			squadRecords.Clear();
		}

		public override void OnPlayerMovedByAdmin(string soldierName, int destinationTeamId, int destinationSquadId, bool forceKilled)
		{
			base.OnPlayerMovedByAdmin(soldierName, destinationTeamId, destinationSquadId, forceKilled);

			OnPlayerLeft(base.FrostbitePlayerInfoList[soldierName], "team");
		}

		public override void OnPlayerTeamChange(string soldierName, int teamId, int squadId)
		{
			base.OnPlayerTeamChange(soldierName, teamId, squadId);

			OnPlayerLeft(base.FrostbitePlayerInfoList[soldierName], "team");
		}

		public override void OnPlayerSquadChange(string soldierName, int teamId, int squadId)
		{
			base.OnPlayerSquadChange(soldierName, teamId, squadId);

			OnPlayerLeft(base.FrostbitePlayerInfoList[soldierName], "squad");
		}

		public override void OnPlayerLeft(CPlayerInfo playerInfo)
		{
			base.OnPlayerLeft(playerInfo);

			OnPlayerLeft(playerInfo, "server");
		}

		public void OnPlayerLeft(CPlayerInfo playerInfo, string reason)
		{
			string name = playerInfo.SoldierName;

			speakerIntent.Remove(name);
			squadRecords.Remove(name);

			if (promotedPlayers.ContainsKey(name))
			{
				string squadLeader = promotedPlayers[name];

				squadRecords.Remove(squadLeader);
				promotedPlayers.Remove(name);

				AdminSayPlayer(String.Format(messages[MessageName.PLAYER_LEFT_DURING_PROMOTION].Value, name, reason), squadLeader);

				ConsoleDebug(name + " has left the " + reason + " while being promoted by " + squadLeader + "! Canceling promotion.");
			}
			else if (promotedPlayers.ContainsValue(name))
			{
				string playerPromoted = null;
				foreach(string s in promotedPlayers.Keys)
					if (promotedPlayers[s].Equals(name))
					{
						playerPromoted = s;
						break;
					}

				if (playerPromoted == null)
				{
					ConsoleError("wat2");
					return;
				}

				squadRecords.Remove(name);
				promotedPlayers.Remove(playerPromoted);

				AdminSayPlayer(String.Format(messages[MessageName.YOUR_PROMOTION_CANCELED_SQUAD_LEADER_LEFT].Value, name, reason), playerPromoted);

				ConsoleDebug(name + " has left the " + reason + " while promoting a player, " + playerPromoted + "! Canceling promotion.");
			}
		}

		public enum MessageType { Warning, Error, Exception, Normal, Debug };

		private string FormatMessage(string msg, MessageType type)
		{
			string prefix = "[^b" + GetPluginName() + "^n] ";

			switch (type)
			{
				case MessageType.Warning:
					prefix += "^1^bWARNING^0^n: ";
					break;
				case MessageType.Error:
					prefix += "^1^bERROR^0^n: ";
					break;
				case MessageType.Exception:
					prefix += "^1^bEXCEPTION^0^n: ";
					break;
				case MessageType.Debug:
					prefix += "^1^bDEBUG^0^n: ";
					break;
			}

			return prefix + msg;
		}

		public void LogWrite(string msg)
		{
			this.ExecuteCommand("procon.protected.pluginconsole.write", msg);
		}

		public void ConsoleWrite(string msg, MessageType type)
		{
			LogWrite(FormatMessage(msg, type));
		}

		public void ConsoleWrite(string msg)
		{
			ConsoleWrite(msg, MessageType.Normal);
		}

		public void ConsoleDebug(string msg)
		{
			if (bools[BoolName.DEBUG_MODE].Value)
				ConsoleWrite(msg, MessageType.Debug);
		}

		public void ConsoleWarn(string msg)
		{
			ConsoleWrite(msg, MessageType.Warning);
		}

		public void ConsoleError(string msg)
		{
			ConsoleWrite(msg, MessageType.Error);
		}

		public void ConsoleException(string msg)
		{
			ConsoleWrite(msg, MessageType.Exception);
		}

		public void AdminSayAll(string msg)
		{
			if (bools[BoolName.DEBUG_MODE].Value)
				ConsoleDebug("Saying to all: " + msg);

			foreach (string s in splitMessage(msg, 128))
				this.ExecuteCommand("procon.protected.send", "admin.say", s, "all");
		}

		public void AdminSayTeam(string msg, int teamID)
		{
			if (bools[BoolName.DEBUG_MODE].Value)
				ConsoleDebug("Saying to Team " + teamID + ": " + msg);

			foreach (string s in splitMessage(msg, 128))
				this.ExecuteCommand("procon.protected.send", "admin.say", s, "team", string.Concat(teamID));
		}

		public void AdminSaySquad(string msg, int teamID, int squadID)
		{
			if (bools[BoolName.DEBUG_MODE].Value)
				ConsoleDebug("Saying to Squad " + squadID + " in Team " + teamID + ": " + msg);

			foreach (string s in splitMessage(msg, 128))
				this.ExecuteCommand("procon.protected.send", "admin.say", s, "squad", string.Concat(teamID), string.Concat(squadID));
		}

		public void AdminSayPlayer(string msg, string player)
		{
			if (bools[BoolName.DEBUG_MODE].Value)
				ConsoleDebug("Saying to player '" + player + "': " + msg);

			foreach (string s in splitMessage(msg, 128))
				this.ExecuteCommand("procon.protected.send", "admin.say", s, "player", player);
		}

		public void AdminYellAll(string msg)
		{
			AdminYellAll(msg, 10);
		}

		public void AdminYellAll(string msg, int duration)
		{
			if (msg.Length > 256)
				ConsoleError("AdminYell msg > 256. msg: " + msg);

			if (bools[BoolName.DEBUG_MODE].Value)
				ConsoleDebug("Yelling to all: " + msg);

			this.ExecuteCommand("procon.protected.send", "admin.yell", msg, string.Concat(duration), "all");
		}

		public void AdminYellTeam(string msg, int teamID)
		{
			AdminYellTeam(msg, teamID, 10);
		}

		public void AdminYellTeam(string msg, int teamID, int duration)
		{
			if (msg.Length > 256)
				ConsoleError("AdminYell msg > 256. msg: " + msg);

			if (bools[BoolName.DEBUG_MODE].Value)
				ConsoleDebug("Yelling to Team " + teamID + ": " + msg);

			this.ExecuteCommand("procon.protected.send", "admin.yell", msg, string.Concat(duration), "team", string.Concat(teamID));
		}

		public void AdminYellSquad(string msg, int teamID, int squadID)
		{
			AdminYellSquad(msg, teamID, squadID, 10);
		}

		public void AdminYellSquad(string msg, int teamID, int squadID, int duration)
		{
			if (msg.Length > 256)
				ConsoleError("AdminYell msg > 256. msg: " + msg);

			if (bools[BoolName.DEBUG_MODE].Value)
				ConsoleDebug("Yelling to Squad " + squadID + " in Team " + teamID + ": " + msg);

			this.ExecuteCommand("procon.protected.send", "admin.yell", msg, string.Concat(duration), "squad", string.Concat(teamID), string.Concat(squadID));
		}

		public void AdminYellPlayer(string msg, string player)
		{
			AdminYellPlayer(msg, player, 10);
		}

		public void AdminYellPlayer(string msg, string player, int duration)
		{
			if (msg.Length > 256)
				ConsoleError("AdminYell msg > 256. msg: " + msg);

			if (bools[BoolName.DEBUG_MODE].Value)
				ConsoleDebug("Yelling to player '" + player + "': " + msg);

			this.ExecuteCommand("procon.protected.send", "admin.yell", msg, string.Concat(duration), "player", player);
		}

		private List<string> splitMessage(string message, int maxSize)
		{
			List<string> messages = new List<string>(message.Replace("\r", "").Trim().Split(new char[] { '\n' }, StringSplitOptions.RemoveEmptyEntries));

			for (int a = 0; a < messages.Count; a++)
			{
				messages[a] = messages[a].Trim();

				if (messages[a] == "")
				{
					messages.RemoveAt(a);
					a--;
					continue;
				}

				if (messages[a][0] == '/')
					messages[a] = ' ' + messages[a];

				string msg = messages[a];

				if (msg.Length > maxSize)
				{
					List<int> splitOptions = new List<int>();
					int split = -1;
					do
					{
						split = msg.IndexOfAny(new char[] { '.', '!', '?', ';' }, split + 1);
						if (split != -1 && split != msg.Length - 1)
							splitOptions.Add(split);
					} while (split != -1);

					if (splitOptions.Count > 2)
						split = splitOptions[(int)Math.Round(splitOptions.Count / 2.0)] + 1;
					else if (splitOptions.Count > 0)
						split = splitOptions[0] + 1;
					else
					{
						split = msg.IndexOf(',');

						if (split == -1)
						{
							split = msg.IndexOf(' ', msg.Length / 2);

							if (split == -1)
							{
								split = msg.IndexOf(' ');

								if (split == -1)
									split = maxSize / 2;
							}
						}
					}

					messages[a] = msg.Substring(0, split).Trim();
					messages.Insert(a + 1, msg.Substring(split).Trim());

					a--;
				}
			}

			return messages;
		}
	}
}
