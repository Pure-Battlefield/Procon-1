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
	public class PureMessage : PRoConPluginAPI, IPRoConPluginInterface
	{
		private enum MessageTrigger
		{
			PLAYER_JOIN, PLAYER_SPAWN, TICKET_COUNT, TIMED_COUNTER
		}

		private enum MessageType
		{
			SAY, YELL, SAY_AND_YELL
		}

		private enum MessageDestination
		{
			PLAYER, ALL
		}

		private abstract class Message
		{
			public PureMessage parent { get; private set;  }

			public virtual bool enabled { get; protected set; }

			public virtual string name { get; set; }
			public virtual MessageType type { get; set; }
			public virtual MessageDestination dest { get; set; }
			public virtual string message { get; set; }

			public Message(Message other)
				: this(other.parent, other.name, other.type, other.dest, other.message)
			{
				enabled = other.enabled;
			}

			public Message(PureMessage parent, string name, MessageType type, MessageDestination dest, string message) 
			{
				this.parent = parent;

				this.name = name;
				this.type = type;
				this.dest = dest;
				this.message = message;
			}

			public virtual List<CPluginVariable> getVariables()
			{
				return getVariables(true, true);
			}

			protected List<CPluginVariable> getVariables(bool showTypeVariable, bool showDestVariable)
			{
				List<CPluginVariable> variables = new List<CPluginVariable>();

				variables.Add(new CPluginVariable(name + "|" + name + " - Message name", "string", name));
				variables.Add(new CPluginVariable(name + "|" + name + " - Message trigger", "enum.MessageTriggerAndRemove(Remove|" + String.Join("|", Enum.GetNames(typeof(MessageTrigger))) + ")", Enum.GetName(typeof(MessageTrigger), getMessageTrigger())));
				variables.Add(new CPluginVariable(name + "|" + name + " - Message enabled", "bool", enabled.ToString()));
				if (showTypeVariable)
					variables.Add(new CPluginVariable(name + "|" + name + " - Message type", "enum.MessageType(" + String.Join("|", Enum.GetNames(typeof(MessageType))) + ")", Enum.GetName(typeof(MessageType), type)));
				if (showDestVariable)
					variables.Add(new CPluginVariable(name + "|" + name + " - Message destination", "enum.MessageDestination(" + String.Join("|", Enum.GetNames(typeof(MessageDestination))) + ")", Enum.GetName(typeof(MessageDestination), dest)));
				variables.Add(new CPluginVariable(name + "|" + name + " - Message body", "multiline", message));

				return variables;
			}

			public virtual void setVariable(string variable, string value)
			{
				if (variable.Contains("Message name"))
				{
					if (value.Contains("-"))
						parent.ConsoleError("Invalid name '" + value + "': cannot use dash");
					else if (value.Contains("|"))
						parent.ConsoleError("Invalid name '" + value + "': cannot use vertical bar/pipe");
					else
						name = value;
				}
				else if (variable.Contains("Message type"))
					type = (MessageType)Enum.Parse(typeof(MessageType), value);
				else if (variable.Contains("Message enabled"))
				{
					enabled = bool.Parse(value);

					if (enabled)
						enable();
					else
						disable();
				}
				else if (variable.Contains("Message destination"))
					dest = (MessageDestination)Enum.Parse(typeof(MessageDestination), value);
				else if (variable.Contains("Message body"))
					message = value;
			}

			public abstract MessageTrigger getMessageTrigger();

			protected void sendMessage(string message, string player)
			{
				switch (dest)
				{
					case MessageDestination.ALL:
						sendMessageAll(message);
						break;
					case MessageDestination.PLAYER:
						if (player == null)
							throw new InvalidOperationException("Player is null!");

						sendMessagePlayer(message, player);
						break;
				}
			}

			protected void sendMessageAll(string message)
			{
				if(dest != MessageDestination.ALL)
					throw new InvalidOperationException("This message can only be sent to a specific player.");

				switch (type)
				{
					case MessageType.SAY:
						parent.AdminSayAll(message);
						break;
					case MessageType.YELL:
						parent.AdminYellAll(message);
						break;
					case MessageType.SAY_AND_YELL:
						parent.AdminSayAll(message);
						parent.AdminYellAll(message);
						break;
				}
			}

			protected void sendMessagePlayer(string message, string player)
			{
				if (dest != MessageDestination.PLAYER)
					throw new InvalidOperationException("This message can only be sent to ALL.");

				switch (type)
				{
					case MessageType.SAY:
						parent.AdminSayPlayer(message, player);
						break;
					case MessageType.YELL:
						parent.AdminYellPlayer(message, player);
						break;
					case MessageType.SAY_AND_YELL:
						parent.AdminSayPlayer(message, player);
						parent.AdminYellPlayer(message, player);
						break;
				}
			}

			public virtual void processTask(params object[] values) { }

			public virtual void enable() { }

			public virtual void disable() { }

			public virtual void OnStartRound(string mapFileName, string gamemode, int roundsPlayed, int roundsTotal) { }

			public virtual void OnEndRound(int winningTeam) { }

			public virtual void OnPlayerJoined(string player) { }

			public virtual void OnPlayerSpawned(string player) { }

			public virtual void OnPlayerKilled(Kill kill) { }

			public virtual void OnPlayerLeft(string player) { }

			public virtual void OnServerInfo(CServerInfo serverInfo) { }

			protected void delayTask(int seconds)
			{
				parent.ExecuteCommand("procon.protected.tasks.add", name, seconds.ToString(), "1", "1", "procon.protected.plugins.call", "PureMessage", "triggerMessage", name);
			}

			protected void delayTask(string append, int seconds)
			{
				parent.ExecuteCommand("procon.protected.tasks.add", name + append, seconds.ToString(), "1", "1", "procon.protected.plugins.call", "PureMessage", "triggerMessage", name);
			}

			protected void delayTask(int seconds, string args)
			{
				parent.ExecuteCommand("procon.protected.tasks.add", name, seconds.ToString(), "1", "1", "procon.protected.plugins.call", "PureMessage", "triggerMessage", name, args);
			}

			protected void delayTask(string append, int seconds, string args)
			{
				parent.ExecuteCommand("procon.protected.tasks.add", name + append, seconds.ToString(), "1", "1", "procon.protected.plugins.call", "PureMessage", "triggerMessage", name, args);
			}

			protected void removeTask()
			{
				parent.ExecuteCommand("procon.protected.tasks.remove", name);
			}

			protected void removeTask(string append)
			{
				parent.ExecuteCommand("procon.protected.tasks.remove", name + append);
			}
		}

		private class PlayerJoinMessage : Message
		{
			public override string name
			{
				set
				{
					foreach (string name in tasksAdded.Keys)
					{
						removeTask(name);

						DateTime taskAdded = tasksAdded[name];

						int secondsDiff = (int)Math.Round((DateTime.Now - taskAdded).TotalSeconds);

						if (secondsDiff < delay)
						{
							int sec = delay - secondsDiff;
							delayTask(name, sec, name);
						}
					}

					base.name = value;
				}
			}

			private static int delay;

			private Dictionary<string, DateTime> tasksAdded = new Dictionary<string, DateTime>();

			public PlayerJoinMessage(Message other) : this(other.parent, other.name, other.type, other.dest, other.message) { }

			public PlayerJoinMessage(PureMessage parent, string name, MessageType type, MessageDestination dest, string message) : base(parent, name, type, dest, message) {}

			public override MessageTrigger getMessageTrigger()
			{
				return MessageTrigger.PLAYER_JOIN;
			}

			public override List<CPluginVariable> getVariables()
			{
				List<CPluginVariable> variables = base.getVariables();

				variables.Add(new CPluginVariable(name + "|" + name + " - Message delay (seconds)", "int", String.Concat(delay)));

				return variables;
			}

			public override void disable()
			{
				foreach (string name in tasksAdded.Keys)
					removeTask(name);

				tasksAdded.Clear();
			}

			public override void setVariable(string variable, string value)
			{
				base.setVariable(variable, value);

				if (variable.Contains("Message delay (seconds)"))
					delay = int.Parse(value);
			}

			public override void OnPlayerJoined(string player)
			{
				delayTask(player, delay, player);
				tasksAdded.Add(player, DateTime.Now);
			}

			public override void processTask(params object[] values)
			{
				string player = (string)values[0];
				sendMessage(String.Format(message, player), player);

				tasksAdded.Remove(player);
			}
		}

		private class PlayerSpawnMessage : Message
		{
			private List<string> playersNotMessaged = new List<string>();
			private List<string> playersMessaged = new List<string>();

			private bool triggerOnce;

			public PlayerSpawnMessage(Message other) : this(other.parent, other.name, other.type, other.dest, other.message) { }

			public PlayerSpawnMessage(PureMessage parent, string name, MessageType type, MessageDestination dest, string message)
				: base(parent, name, type, dest, message)
			{
				this.triggerOnce = true;
			}

			public override MessageTrigger getMessageTrigger()
			{
				return MessageTrigger.PLAYER_SPAWN;
			}

			public override List<CPluginVariable> getVariables()
			{
				List<CPluginVariable> variables = base.getVariables();

				variables.Add(new CPluginVariable(name + "|" + name + " - Trigger options", "enum.TriggerFrequency(Only first spawn|Every spawn)", triggerOnce ? "Only first spawn" : "Every spawn"));

				return variables;
			}

			public override void setVariable(string variable, string value)
			{
				base.setVariable(variable, value);

				if (variable.Contains("Trigger options"))
				{
					triggerOnce = value.Equals("Only first spawn");

					if (triggerOnce)
					{
						playersMessaged.Clear();

						foreach (string name in parent.FrostbitePlayerInfoList.Keys)
							if (!playersNotMessaged.Contains(name))
								playersMessaged.Add(name);
					}
					else
						playersMessaged.Clear();
				}
			}

			public override void OnPlayerJoined(string player)
			{
				playersNotMessaged.Add(player);
			}

			public override void OnPlayerSpawned(string player)
			{
				if (!playersMessaged.Contains(player))
				{
					sendMessage(String.Format(message, player), player);

					playersNotMessaged.Remove(player);

					if (triggerOnce)
						playersMessaged.Add(player);
				}
			}

			public override void OnPlayerLeft(string player)
			{
				playersNotMessaged.Remove(player);
				playersMessaged.Remove(player);
			}
		}

		private class TicketCountMessage : Message
		{
			private static int startTicketCount = -1;

			private bool triggered = false;
			private int ticketCountPercent;

			public TicketCountMessage(Message other) : this(other.parent, other.name, other.type, other.message, 0) { }

			public TicketCountMessage(PureMessage parent, string name, MessageType type, string message, int ticketCountPercent)
				: base(parent, name, type, MessageDestination.ALL, message)
			{
				this.ticketCountPercent = ticketCountPercent;
			}

			public override MessageTrigger getMessageTrigger()
			{
				return MessageTrigger.TICKET_COUNT;
			}

			public override List<CPluginVariable> getVariables()
			{
				List<CPluginVariable> variables = getVariables(true, false);
				variables.Add(new CPluginVariable(name + "|" + name + " - Ticket Count Percent", "int", String.Concat(ticketCountPercent)));
				return variables;
			}

			public override void setVariable(string variable, string value)
			{
				base.setVariable(variable, value);

				if (variable.Contains("Ticket Count Percent"))
					ticketCountPercent = int.Parse(value);
			}

			public override void OnEndRound(int winningTeam)
			{
				startTicketCount = -1;
				triggered = false;
			}

			public override void OnServerInfo(CServerInfo serverInfo)
			{
				enabled = serverInfo.GameMode.ToLower().Contains("conquest");

				if (!enabled)
				{
					parent.ExecuteCommand("procon.protected.plugins.setVariable", "PureMessage", "IGNORE", "IGNORE");
					parent.ConsoleWrite("Ticket Count Message '" + name + "' only works in Conquest.");
					return;
				}

				if (serverInfo.TeamScores.Count < 2)
					return;

				if (startTicketCount == -1)
					startTicketCount = serverInfo.TeamScores[0].Score;

				if (!triggered && (serverInfo.TeamScores[0].Score <= (ticketCountPercent / 100.0) * startTicketCount || serverInfo.TeamScores[1].Score <= (ticketCountPercent / 100.0) * startTicketCount))
				{
					sendMessage(String.Format(message, ticketCountPercent), null);
					triggered = true;
				}
			}

			public override void enable()
			{
				parent.ConsoleWrite("Ticket Count Message '" + name + "' requires a round restart to grab the initial ticket count.");
			}

			public override void disable()
			{
				triggered = false;
			}
		}

		private class TimedCounterMessage : Message
		{
			private DateTime taskAdded = default(DateTime);

			public override string name
			{
				set
				{
					int secondsDiff = taskAdded == default(DateTime) ? -1 : (int)Math.Round((DateTime.Now - taskAdded).TotalSeconds);

					disable();

					base.name = value;

					if (secondsDiff > -1 && secondsDiff < seconds)
					{
						seconds -= secondsDiff;
						OnStartRound(null, null, 0, 0);
						seconds += secondsDiff;
					}
				}
			}

			public int seconds { get; set; }

			public TimedCounterMessage(Message other) : this(other.parent, other.name, other.type, other.message, 0) { }

			public TimedCounterMessage(PureMessage parent, string name, MessageType type, string message, int seconds)
				: base(parent, name, type, MessageDestination.ALL, message)
			{
				this.seconds = seconds;
			}

			public override MessageTrigger getMessageTrigger()
			{
				return MessageTrigger.TIMED_COUNTER;
			}

			public override List<CPluginVariable> getVariables()
			{
				List<CPluginVariable> variables = getVariables(true, false);
				variables.Add(new CPluginVariable(name + "|" + name + " - Seconds from start of round", "int", String.Concat(seconds)));
				return variables;
			}

			public override void setVariable(string variable, string value)
			{
				base.setVariable(variable, value);

				if (variable.Contains("Seconds from start of round"))
					seconds = int.Parse(value);
			}

			public override void processTask(params object[] values)
			{
				sendMessage(message, null);
			}

			public override void OnStartRound(string mapFileName, string gamemode, int roundsPlayed, int roundsTotal)
			{
				if (taskAdded != default(DateTime))
					return;

				parent.ConsoleDebug("Enabling procon task for " + name + " at " + seconds + " seconds.");
				delayTask(seconds);

				taskAdded = DateTime.Now;
			}

			public override void OnEndRound(int winningTeam)
			{
				if (taskAdded == null)
					return;

				parent.ConsoleDebug("Disabling procon task for " + name + " at " + seconds + " seconds.");
				removeTask();

				taskAdded = default(DateTime);
			}

			public override void enable()
			{
				parent.ConsoleWrite("Timed Counter '" + name + "' will be enabled at the start of the next round.");
			}

			public override void disable()
			{
				OnEndRound(0);
			}
		}

		private bool debug = true;
		private bool pluginEnabled = false;

		private List<Message> messages;

		public PureMessage()
		{
			messages = new List<Message>();
		}

		public enum ConsoleMessageType { Warning, Error, Exception, Normal, Debug };

		private string FormatMessage(string msg, ConsoleMessageType type)
		{
			string prefix = "[^b" + GetPluginName() + "^n] ";

			switch(type)
			{
				case ConsoleMessageType.Warning:
					prefix += "^1^bWARNING^0^n: ";
					break;
				case ConsoleMessageType.Error:
					prefix += "^1^bERROR^0^n: ";
					break;
				case ConsoleMessageType.Exception:
					prefix += "^1^bEXCEPTION^0^n: ";
					break;
				case ConsoleMessageType.Debug:
					prefix += "^1^bDEBUG^0^n: ";
					break;
			}

			return prefix + msg;
		}

		public void LogWrite(string msg)
		{
			this.ExecuteCommand("procon.protected.pluginconsole.write", msg);
		}

		public void ConsoleWrite(string msg, ConsoleMessageType type)
		{
			LogWrite(FormatMessage(msg, type));
		}

		public void ConsoleWrite(string msg)
		{
			ConsoleWrite(msg, ConsoleMessageType.Normal);
		}

		public void ConsoleDebug(string msg)
		{
			if (debug)
				ConsoleWrite(msg, ConsoleMessageType.Debug);
		}

		public void ConsoleWarn(string msg)
		{
			ConsoleWrite(msg, ConsoleMessageType.Warning);
		}

		public void ConsoleError(string msg)
		{
			ConsoleWrite(msg, ConsoleMessageType.Error);
		}

		public void ConsoleException(string msg)
		{
			ConsoleWrite(msg, ConsoleMessageType.Exception);
		}

		public void AdminSayAll(string msg)
		{
			if (debug)
				ConsoleDebug("Saying to all: " + msg);

			foreach (string s in splitMessage(msg, 128))
				this.ExecuteCommand("procon.protected.send", "admin.say", s, "all");
		}

		public void AdminSayTeam(string msg, int teamID)
		{
			if (debug)
				ConsoleDebug("Saying to Team " + teamID + ": " + msg);

			foreach (string s in splitMessage(msg, 128))
				this.ExecuteCommand("procon.protected.send", "admin.say", s, "team", string.Concat(teamID));
		}

		public void AdminSaySquad(string msg, int teamID, int squadID)
		{
			if (debug)
				ConsoleDebug("Saying to Squad " + squadID + " in Team " + teamID + ": " + msg);

			foreach (string s in splitMessage(msg, 128))
				this.ExecuteCommand("procon.protected.send", "admin.say", s, "squad", string.Concat(teamID), string.Concat(squadID));
		}

		public void AdminSayPlayer(string msg, string player)
		{
			if (debug)
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

			if (debug)
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

			if (debug)
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

			if (debug)
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

			if (debug)
				ConsoleDebug("Yelling to player '" + player + "': " + msg);

			this.ExecuteCommand("procon.protected.send", "admin.yell", msg, string.Concat(duration), "player", player);
		}

		private static List<string> splitMessage(string message, int maxSize)
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

		public string GetPluginName()
		{
			return "pureMessage";
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
			return @"<h1>PureMessage</h1>
					<p>
					Both PLAYER_JOIN and PLAYER_SPAWN are offered 1 replaceable command in the message: {0} = player name
					</p>					
					
					<p>
					TICKET_COUNT (Conquest-only message) requires the round to be restarted as it needs to grab the starting ticket count.<br />
					The message is given 1 replaceable command in the message: {0} = percent value
					</p>
					
					<p>TIMED_COUNTER is enabled at start of round only. If you add one mid-round, it'll start the timer the next round.</p>
					
					<p>Admin.yell cannot exceed 256 characters</p>
					
					<p>Admin.Say messages may exceed 128 characters; they are appropriately split up by, in order of precedence: newlines, end-of-sentence punctuation marks, commas, spaces, or arbitrarily.</p>
					";
		}

		public void OnPluginLoaded(string strHostName, string strPort, string strPRoConVersion)
		{
			this.RegisterEvents(this.GetType().Name, "OnServerInfo", "OnEndRound", "OnRunNextLevel", "OnRestartLevel", "OnRoundOver", "OnLevelLoaded", "OnPlayerJoin", "OnPlayerLeft", "OnPlayerSpawned", "OnPlayerKilled");
		}

		public void OnPluginEnable()
		{
			this.pluginEnabled = true;

			foreach (Message m in messages.FindAll((m) => !m.enabled))
				m.enable();

			ConsoleWrite("^2" + GetPluginName() + " Enabled");
		}

		public void OnPluginDisable()
		{
			this.pluginEnabled = false;

			foreach (Message m in messages.FindAll((m) => m.enabled))
				m.disable();

			ConsoleWrite("^8" + GetPluginName() + " Disabled");
		}

		public List<CPluginVariable> GetDisplayPluginVariables()
		{
			List<CPluginVariable> variables = new List<CPluginVariable>();

			foreach(Message m in messages)
				variables.AddRange(m.getVariables());

			variables.Add(new CPluginVariable("Add New/Settings|Debug", "bool", debug.ToString()));
			variables.Add(new CPluginVariable("Add New/Settings|Add a message", "enum.MessageTrigger(Choose...|" + String.Join("|", Enum.GetNames(typeof(MessageTrigger))) + ")", "Choose..."));

			return variables;
		}

		public List<CPluginVariable> GetPluginVariables()
		{
			return GetDisplayPluginVariables();
		}

		public void SetPluginVariable(string variable, string value)
		{
			if (variable.Equals("IGNORE"))
				return;

			ConsoleDebug(variable + " = " + value);

			if (variable.Contains("Debug"))
				debug = bool.Parse(value);
			else if (variable.Contains("Add a message"))
			{
				if (value.Equals("Choose..."))
					return;

				Message message = null;

				switch ((MessageTrigger)Enum.Parse(typeof(MessageTrigger), value))
				{
					case MessageTrigger.PLAYER_JOIN:
						message = new PlayerJoinMessage(this, "Player Join Message #" + messages.Count, MessageType.SAY, MessageDestination.ALL, "");
						break;
					case MessageTrigger.PLAYER_SPAWN:
						message = new PlayerSpawnMessage(this, "Player Spawn Message #" + messages.Count, MessageType.SAY, MessageDestination.ALL, "");
						break;
					case MessageTrigger.TICKET_COUNT:
						message = new TicketCountMessage(this, "Ticket Count Message #" + messages.Count, MessageType.SAY, "", 0);
						break;
					case MessageTrigger.TIMED_COUNTER:
						message = new TimedCounterMessage(this, "Timed Counter Message #" + messages.Count, MessageType.SAY, "", 0);
						break;
				}

				messages.Add(message);
				message.enable();
			}
			else if (variable.Contains("trigger"))
			{
				string name = variable.Substring(0, variable.IndexOf(" - ")).Trim();

				if (name.Contains("|"))
					name = name.Substring(0, name.IndexOf("|")).Trim();

				if (value.Equals("Remove"))
				{
					Message toRemove = messages.Find((m) => m.name.Equals(name));
					toRemove.disable();

					messages.Remove(toRemove);
				}
				else
				{
					int toChangeLoc = -1;
					for (int a = 0; a < messages.Count; a++)
						if (messages[a].name.Equals(name))
						{
							toChangeLoc = a;
							break;
						}

					if (toChangeLoc == -1)
					{
						//looks like an un-added message, must be loading settings

						Message message = null;

						switch ((MessageTrigger)Enum.Parse(typeof(MessageTrigger), value))
						{
							case MessageTrigger.PLAYER_JOIN:
								message = new PlayerJoinMessage(this, name, MessageType.SAY, MessageDestination.ALL, "");
								break;
							case MessageTrigger.PLAYER_SPAWN:
								message = new PlayerSpawnMessage(this, name, MessageType.SAY, MessageDestination.ALL, "");
								break;
							case MessageTrigger.TICKET_COUNT:
								message = new TicketCountMessage(this, name, MessageType.SAY, "", 0);
								break;
							case MessageTrigger.TIMED_COUNTER:
								message = new TimedCounterMessage(this, name, MessageType.SAY, "", 0);
								break;
						}

						messages.Add(message);
						message.enable();
					}
					else
					{
						messages[toChangeLoc].disable();

						switch ((MessageTrigger)Enum.Parse(typeof(MessageTrigger), value))
						{
							case MessageTrigger.PLAYER_JOIN:
								if (messages[toChangeLoc].GetType() == typeof(PlayerJoinMessage))
									break;

								messages[toChangeLoc] = new PlayerJoinMessage(messages[toChangeLoc]);
								break;
							case MessageTrigger.PLAYER_SPAWN:
								if (messages[toChangeLoc].GetType() == typeof(PlayerSpawnMessage))
									break;

								messages[toChangeLoc] = new PlayerSpawnMessage(messages[toChangeLoc]);
								break;
							case MessageTrigger.TICKET_COUNT:
								if (messages[toChangeLoc].GetType() == typeof(TicketCountMessage))
									break;

								messages[toChangeLoc] = new TicketCountMessage(messages[toChangeLoc]);
								break;
							case MessageTrigger.TIMED_COUNTER:
								if (messages[toChangeLoc].GetType() == typeof(TimedCounterMessage))
									break;

								messages[toChangeLoc] = new TimedCounterMessage(messages[toChangeLoc]);
								break;
						}

						messages[toChangeLoc].enable();
					}
				}
			}
			else
			{
				string name = variable.Substring(0, variable.IndexOf(" - ")).Trim();

				if (name.Contains("|"))
					name = name.Substring(0, name.IndexOf("|")).Trim();

				Message message = messages.Find((m) => m.name.Equals(name));

				if (message == null)
				{
					if (name.Contains("|"))
						name = name.Substring(0, name.IndexOf("|")).Trim();

					//looks like an un-added message, must be loading settings
					message = new PlayerSpawnMessage(this, name, MessageType.SAY, MessageDestination.ALL, "");
					messages.Add(message);
					message.enable();
				}

				message.setVariable(variable, value);
			}
		}

		public override void OnServerInfo(CServerInfo serverInfo)
		{
			foreach (Message m in messages.FindAll((m) => m.enabled))
				try
				{
					m.OnServerInfo(serverInfo);
				}
				catch (Exception exc)
				{
					ConsoleError("Exception with OnServerInfo of '" + m.name + "': " + exc.Message + " " + exc.StackTrace);
				}
		}

		public override void OnEndRound(int iWinningTeamID)
		{
			foreach (Message m in messages.FindAll((m) => m.enabled))
				try
				{
					m.OnEndRound(iWinningTeamID);
				}
				catch (Exception exc)
				{
					ConsoleError("Exception with OnEndRound of '" + m.name + "': " + exc.Message + " " + exc.StackTrace);
				}
		}

		public override void OnRunNextLevel()
		{
			OnEndRound(0);
		}

		public override void OnRestartLevel()
		{
			OnEndRound(0);
		}

		public override void OnRoundOver(int iWinningTeamID)
		{
			OnEndRound(iWinningTeamID);
		}

		public override void OnLevelLoaded(string mapFileName, string gamemode, int roundsPlayed, int roundsTotal)
		{
			foreach (Message m in messages.FindAll((m) => m.enabled))
				try
				{
					m.OnStartRound(mapFileName, gamemode, roundsPlayed, roundsTotal);
				}
				catch (Exception exc)
				{
					ConsoleError("Exception with OnLevelLoaded/OnLevelStarted of '" + m.name + "': " + exc.Message + " " + exc.StackTrace);
				}
		}
		
		public void triggerMessage(string name, params object[] values)
		{
			ConsoleDebug("Trigger message on " + name);

			try
			{
				messages.Find((m) => m.enabled && m.name.Equals(name)).processTask(values);
			}
			catch (Exception exc)
			{
				ConsoleError("Exception with triggerMessage on '" + name + "': " + exc.Message + " " + exc.StackTrace);
			}
		}

		public void triggerMessage(string name)
		{
			ConsoleDebug("Trigger message on " + name);

			triggerMessage(name, null);
		}

		public override void OnPlayerJoin(string soldierName)
		{
			base.OnPlayerJoin(soldierName);

			foreach (Message m in messages.FindAll((m) => m.enabled))
				try
				{
					m.OnPlayerJoined(soldierName);
				}
				catch (Exception exc)
				{
					ConsoleError("Exception with OnPlayerJoined of '" + m.name + "': " + exc.Message + " " + exc.StackTrace);
				}
		}

		public override void OnPlayerLeft(CPlayerInfo playerInfo)
		{
			base.OnPlayerLeft(playerInfo);

			foreach (Message m in messages.FindAll((m) => m.enabled))
				try 
				{
					m.OnPlayerLeft(playerInfo.SoldierName);
				}
				catch (Exception exc)
				{
					ConsoleError("Exception with OnPlayerLeft of '" + m.name + "': " + exc.Message + " " + exc.StackTrace);
				}
		}

		public override void OnPlayerSpawned(string soldierName, Inventory spawnedInventory)
		{
			base.OnPlayerSpawned(soldierName, spawnedInventory);

			foreach (Message m in messages.FindAll((m) => m.enabled))
				try
				{
					m.OnPlayerSpawned(soldierName);
				}
				catch (Exception exc)
				{
					ConsoleError("Exception with OnPlayerSpawned of '" + m.name + "': " + exc.Message + " " + exc.StackTrace);
				}
		}

		public override void OnPlayerKilled(Kill kKillerVictimDetails)
		{
			base.OnPlayerKilled(kKillerVictimDetails);

			foreach (Message m in messages.FindAll((m) => m.enabled))
				try
				{
					m.OnPlayerKilled(kKillerVictimDetails);
				}
				catch (Exception exc)
				{
					ConsoleError("Exception with OnPlayerKilled of '" + m.name + "': " + exc.Message + " " + exc.StackTrace);
				}
		}
	}
}
