﻿/**
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
    public class Surrender : PRoConPluginAPI, IPRoConPluginInterface
    {
        private bool pluginEnabled = false;

        private enum VariableName {
            TIME_TO_VOTE,
            TIMEOUT,
            MIN_PLAYERS,
            MIN_TICKET_GAP,
            MIN_PERCENT_TICKET_REMAINING,
            PERCENT_VOTE,
            VOTING_BEGINS_YELL_DURATION,
            VOTING_SUCCESS_YELL_DURATION,
            SURRENDER_VOTING_STATS_INTERVAL,
            DELAY_ENDROUND
        }

        private enum BoolName
        {
            SAY_VOTING_BEGINS_TO_ALL,
            USE_FORWARD_SLASH_ONLY,
            DEBUG_MODE
        }

        private enum MessageName
        {
            WRONG_GAMEMODE,
            LOSING_NO_MORE,
            NOT_ON_LOSING_TEAM,
            TOO_SOON,
            NOT_ENOUGH_PLAYERS,
            TOO_LOW_TICKET_COUNT,
            TOO_LOW_TICKET_GAP,
            SURRENDER_VOTING_BEGINS,
            ALREADY_VOTED,
            SURRENDER_VOTING_PASSES,
            SURRENDER_VOTING_STATS,
            SURRENDER_VOTE_PLAYER_MESSAGE,
            NO_SURRENDER_VOTING,
            VOTING_FAILED
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

        private Dictionary<VariableName, Variable<int>> variables;
        private Dictionary<BoolName, Variable<bool>> bools;
        private Dictionary<MessageName, Variable<string>> messages;

        private bool isConquest;

        private CServerInfo serverInfo;
        private DateTime roundStartTime;
        private int startTicketCount = -1;

        private Thread surrenderTimeoutCountdown;
        private DateTime surrenderVotingStart;
        private List<string> votedNames;
        private int votesNeeded;
        private int surrenderingTeamID = -1;

        public Surrender()
        {
            variables = new Dictionary<VariableName, Variable<int>>();
            variables.Add(VariableName.TIME_TO_VOTE, new Variable<int>("Variables|Time (in seconds) until surrender available", 180));
            variables.Add(VariableName.TIMEOUT, new Variable<int>("Variables|Time (in seconds) until surrender expires", 180));
            variables.Add(VariableName.MIN_PLAYERS, new Variable<int>("Variables|Minimum required number of players on server to surrender", 16));
            variables.Add(VariableName.MIN_TICKET_GAP, new Variable<int>("Variables|Minimum ticket gap between two teams", 80));
            variables.Add(VariableName.MIN_PERCENT_TICKET_REMAINING, new Variable<int>("Variables|Percent minimum of tickets remaining", 20));
            variables.Add(VariableName.PERCENT_VOTE, new Variable<int>("Variables|Percent of team required to vote", 30));
            variables.Add(VariableName.VOTING_BEGINS_YELL_DURATION, new Variable<int>("Variables|Duration of yell when voting begins", 10));
            variables.Add(VariableName.VOTING_SUCCESS_YELL_DURATION, new Variable<int>("Variables|Duration of yell when voting is successful", 10));
            variables.Add(VariableName.DELAY_ENDROUND, new Variable<int>("Variables|Time (in seconds) delay between successful surrender and end of round", 5));
            variables.Add(VariableName.SURRENDER_VOTING_STATS_INTERVAL, new Variable<int>("Variables|Print stats every N seconds, 0 = on every vote", 0));

            bools = new Dictionary<BoolName, Variable<bool>>();
            bools.Add(BoolName.SAY_VOTING_BEGINS_TO_ALL, new Variable<bool>("Variables|Say and/or yell 'surrender voting begins' to all (true) or team (false)", true));
            bools.Add(BoolName.USE_FORWARD_SLASH_ONLY, new Variable<bool>("Variables|Use foward slash only (true) or allow exclamation mark (!) and at symbol (@) (false)", true));
            bools.Add(BoolName.DEBUG_MODE, new Variable<bool>("Variables|Debug mode", false));

            messages = new Dictionary<MessageName, Variable<string>>();
            messages.Add(MessageName.WRONG_GAMEMODE, new Variable<string>("Messages|Wrong gamemode message", "You can't surrender while playing this game mode."));
            messages.Add(MessageName.LOSING_NO_MORE, new Variable<string>("Messages|Not losing anymore, surrender voting ending", "Looks like the losing team has now become the winning team! Surrender voting ending."));
            messages.Add(MessageName.NOT_ON_LOSING_TEAM, new Variable<string>("Messages|Not on losing team", "You're not on the losing team, how can you surrender?"));
            messages.Add(MessageName.TOO_SOON, new Variable<string>("Messages|Surrender not available, too soon to vote ({0} = time)", "Minimum time threshold of {0} has not passed yet to surrender."));
            messages.Add(MessageName.NOT_ENOUGH_PLAYERS, new Variable<string>("Messages|Not enough players ({0} = minimum players needed)", "Number of players on this server must be at least {0} players to surrender."));
            messages.Add(MessageName.TOO_LOW_TICKET_COUNT, new Variable<string>("Messages|Too low ticket count remaining ({0} = minimum ticket count)", "Losing team must have at least {0} tickets remaining to surrender."));
            messages.Add(MessageName.TOO_LOW_TICKET_GAP, new Variable<string>("Messages|Too low ticket gap ({0} = minimum ticket gap)", "Minimum ticket gap between the two teams must be {0} tickets to surrender."));
            messages.Add(MessageName.SURRENDER_VOTING_BEGINS, new Variable<string>("Messages|Surrender voting begins ({0} = votes needed, {1} = time left)", "Surrender voting begins! {0} players must vote within {1} by typing /surrender in chat for vote to pass!"));
            messages.Add(MessageName.ALREADY_VOTED, new Variable<string>("Messages|Already voted", "You've already voted!"));
            messages.Add(MessageName.SURRENDER_VOTING_PASSES, new Variable<string>("Messages|Surrender voting successful ({0} = vote count)", "Surrender voting passed with {0} votes! Losing team has surrendered to the winning team, ending round..."));
            messages.Add(MessageName.SURRENDER_VOTING_STATS, new Variable<string>("Messages|Surrender voting stats ({0} = vote count, {1} = votes needed, {2} = time left)", "Surrender voting: {0}/{1}. {2} left to vote!"));
            messages.Add(MessageName.SURRENDER_VOTE_PLAYER_MESSAGE, new Variable<string>("Messages|Message to player after they vote ({0} = vote count, {1} = votes needed, {2} = time left, {3} = player name)", "{3}, you voted to surrender. Surrender voting: {0}/{1}. {2} left to vote!"));
            messages.Add(MessageName.NO_SURRENDER_VOTING, new Variable<string>("Messages|No surrender voting going on", "No surrender voting going on at the moment."));
            messages.Add(MessageName.VOTING_FAILED, new Variable<string>("Messages|Surrender voting failed ({0} = vote count, {1} votes needed)", "Surrender voting failed! {0}/{1} votes were cast."));
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
                        if(split != -1 && split != msg.Length - 1)
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

                                if(split == -1)
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
            return "Surrender Plugin";
        }

        public string GetPluginVersion()
        {
            return "1.2.7";
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
            return @"
        Allows ability for losing team to surrender. Only Conquest is supported now.
        <br />
        <h2>Commands</h2>
        <table>
        <tr><td><b>/surrender<b></td><td>Initiates or adds a vote to current surrender vote.</td></tr>
        <tr><td><b>/surrenderstatus<b></td><td>Prints vote count, votes needed, and time left of current surrender vote.</td></tr>
        </table>
        <br />
        <br />
        <p>
        Admin.Yell messages may not exceed 256 characters.
        <br />
        Admin.Say messages can exceed 128 characters, they are appropriately split up by, in order of precedence: newlines, end-of-sentence punctuation marks, commas, spaces, or arbitrarily.
        </p>
        <br />
        <p>
        Make sure to restart the current round after enabling this plugin, in order to get the correct initial ticket count.
        </p>
        ";
        }

        public void OnPluginLoaded(string strHostName, string strPort, string strPRoConVersion)
        {
            this.RegisterEvents(this.GetType().Name, "OnGlobalChat", "OnTeamChat", "OnSquadChat", "OnLevelLoaded", "OnRoundOver", "OnRestartLevel", "OnRunNextLevel", "OnEndRound", "OnListPlayers", "OnPlayerJoin", "OnPlayerLeft", "OnServerInfo");
        }

        public void OnPluginEnable()
        {
            this.pluginEnabled = true;

            resetVoting();

            ConsoleWrite("Surrender Plugin Enabled");
        }

        public void OnPluginDisable()
        {
            this.pluginEnabled = false;

            resetVoting();

            ConsoleWrite("Surrender Plugin Disabled");
        }

        public List<CPluginVariable> GetDisplayPluginVariables()
        {
            List<CPluginVariable> pluginVariables = new List<CPluginVariable>();

            foreach (VariableName name in variables.Keys)
                pluginVariables.Add(new CPluginVariable(variables[name].Description, "int", string.Concat(variables[name].Value)));
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
            foreach(VariableName name in variables.Keys) {
                Variable<int> v = variables[name];

                if (v.Description.Contains(strVariable))
                {
                    try
                    {
                        v.Value = int.Parse(strValue);
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

        public override void OnGlobalChat(string speaker, string message)
        {
            if (!pluginEnabled)
                return;

            if (speaker.ToLower().Equals("server"))
                return;

            message = message.ToLower();

            if (!message.Contains("surrender"))
                return;

            lock (this)
            {
                if (Regex.Match(message, @"^[/@!]surrenderstatus", RegexOptions.IgnoreCase).Success)
                {
                    if (bools[BoolName.USE_FORWARD_SLASH_ONLY].Value && !message.Equals("/surrenderstatus"))
                    {
                        AdminSayPlayer("Did you mean /surrenderstatus?", speaker);
                        return;
                    }

                    ConsoleWrite("Player '" + speaker + "' has typed /surrenderstatus");

                    if (!isConquest)
                    {
                        AdminSayPlayer(messages[MessageName.WRONG_GAMEMODE].Value, speaker);
                        return;
                    }

                    if (surrenderTimeoutCountdown == null || this.FrostbitePlayerInfoList[speaker].TeamID != surrenderingTeamID)
                    {
                        AdminSayPlayer(messages[MessageName.NO_SURRENDER_VOTING].Value, speaker);
                        return;
                    }
                    else
                    {
                        int leftTime = variables[VariableName.TIMEOUT].Value - (int)(DateTime.Now - surrenderVotingStart).TotalSeconds;
                        AdminSayPlayer(String.Format(messages[MessageName.SURRENDER_VOTING_STATS].Value, votedNames.Count, votesNeeded, formatTime(leftTime)), speaker);
                    }
                }
                else if (Regex.Match(message, @"^[/@!]surrender", RegexOptions.IgnoreCase).Success)
                {
                    if (bools[BoolName.USE_FORWARD_SLASH_ONLY].Value && !message.Equals("/surrender"))
                    {
                        AdminSayPlayer("Did you mean /surrender?", speaker);
                        return;
                    }

                    ConsoleWrite("Player '" + speaker + "' has typed /surrender");

                    if (!isConquest)
                    {
                        AdminSayPlayer(messages[MessageName.WRONG_GAMEMODE].Value, speaker);
                        return;
                    }

                    if (serverInfo == null)
                    {
                        ConsoleError("SERVERINFO VARIABLE NULL! HOW?!"); //I blame PRoCon
                        return;
                    }

                    if (startTicketCount == -1)
                    {
                        ConsoleError("StartTicketCount == -1! HOW?!"); //I still blame PRoCon
                        return;
                    }

                    int losingTeamID = -1, winningTeamID = -1, losingTeamScore = int.MaxValue, winningTeamScore = -1;
                    foreach (TeamScore team in serverInfo.TeamScores)
                    {
                        if (team.Score < losingTeamScore)
                        {
                            losingTeamID = team.TeamID;
                            losingTeamScore = team.Score;
                        }
                        if (team.Score > winningTeamScore)
                        {
                            winningTeamID = team.TeamID;
                            winningTeamScore = team.Score;
                        }
                    }

                    if (surrenderTimeoutCountdown != null && losingTeamID != surrenderingTeamID)
                    {
                        AdminSayTeam(messages[MessageName.LOSING_NO_MORE].Value, surrenderingTeamID);
                        ConsoleWrite("Losing team is now winning team. Surrender voting ending.");
                        resetVoting();
                        return;
                    }

                    if (losingTeamID != winningTeamID)
                    {
                        ConsoleDebug("Losing team ID: " + losingTeamID + " with score: " + losingTeamScore);
                        ConsoleDebug("Winning team ID: " + winningTeamID + " with score: " + winningTeamScore);
                    }

                    CPlayerInfo player = this.FrostbitePlayerInfoList[speaker];

                    ConsoleDebug("Player '" + speaker + "' has team ID: " + player.TeamID + " and surrendering team ID is: " + surrenderingTeamID);

                    if (player.TeamID != losingTeamID || losingTeamScore == winningTeamScore)
                    {
                        AdminSayPlayer(messages[MessageName.NOT_ON_LOSING_TEAM].Value, speaker);
                        return;
                    }

                    if (surrenderTimeoutCountdown == null)
                    {
                        surrenderingTeamID = losingTeamID;

                        int timeToVote = variables[VariableName.TIME_TO_VOTE].Value;
                        int timePassed = (int)(DateTime.Now - roundStartTime).TotalSeconds;

                        ConsoleDebug("Time started: " + roundStartTime + ", time passed: " + formatTime(timePassed) + ", time to vote: " + formatTime(timeToVote));

                        if (timePassed < timeToVote)
                        {
                            AdminSayPlayer(String.Format(messages[MessageName.TOO_SOON].Value, formatTime(timeToVote)), speaker);
                            return;
                        }

                        int minPlayers = variables[VariableName.MIN_PLAYERS].Value;
                        if (serverInfo.PlayerCount < minPlayers)
                        {
                            AdminSayPlayer(String.Format(messages[MessageName.NOT_ENOUGH_PLAYERS].Value, minPlayers), speaker);
                            return;
                        }

                        int minPercentTicketRemaining = variables[VariableName.MIN_PERCENT_TICKET_REMAINING].Value;
                        if (((double)losingTeamScore / startTicketCount) * 100 < minPercentTicketRemaining)
                        {
                            AdminSayPlayer(String.Format(messages[MessageName.TOO_LOW_TICKET_COUNT].Value, (minPercentTicketRemaining * startTicketCount) / 100), speaker);
                            return;
                        }

                        int minTicketGap = variables[VariableName.MIN_TICKET_GAP].Value;
                        if (winningTeamScore - losingTeamScore < minTicketGap)
                        {
                            AdminSayPlayer(String.Format(messages[MessageName.TOO_LOW_TICKET_GAP].Value, minTicketGap), speaker);
                            return;
                        }

                        surrenderVotingStart = DateTime.Now;

                        surrenderTimeoutCountdown = new Thread(timeoutCountdown);
                        surrenderTimeoutCountdown.Start();

                        votedNames = new List<string>();
                        votedNames.Add(speaker);

                        int percentVote = variables[VariableName.PERCENT_VOTE].Value;
                        int timeout = variables[VariableName.TIMEOUT].Value;

                        int teamPlayerCount = 0;
                        foreach (CPlayerInfo p in this.FrostbitePlayerInfoList.Values)
                            if (p.TeamID == surrenderingTeamID)
                                teamPlayerCount++;

                        ConsoleDebug("There are " + teamPlayerCount + " players in surrendering team with ID: " + surrenderingTeamID);

                        votesNeeded = (int)Math.Round((percentVote * teamPlayerCount) / 100.0);

                        if (bools[BoolName.SAY_VOTING_BEGINS_TO_ALL].Value)
                        {
                            string msg = String.Format(messages[MessageName.SURRENDER_VOTING_BEGINS].Value, votesNeeded, formatTime(timeout));
                            AdminSayAll(msg);
                            if (variables[VariableName.VOTING_BEGINS_YELL_DURATION].Value > 0)
                                AdminYellAll(msg, variables[VariableName.VOTING_BEGINS_YELL_DURATION].Value);
                        }
                        else
                        {
                            string msg = String.Format(messages[MessageName.SURRENDER_VOTING_BEGINS].Value, votesNeeded, formatTime(timeout));
                            AdminSayTeam(msg, surrenderingTeamID);
                            if (variables[VariableName.VOTING_BEGINS_YELL_DURATION].Value > 0)
                                AdminYellTeam(msg, surrenderingTeamID, variables[VariableName.VOTING_BEGINS_YELL_DURATION].Value);
                        }
                        
                        ConsoleWrite("Surrender voting begins by Team " + surrenderingTeamID + " with " + votesNeeded + " votes needed.");
                    }
                    else
                    {
                        if (votedNames.Contains(speaker))
                        {
                            AdminSayPlayer(messages[MessageName.ALREADY_VOTED].Value, speaker);
                            return;
                        }

                        votedNames.Add(speaker);

                        if (votedNames.Count >= votesNeeded)
                        {
                            AdminSayAll(String.Format(messages[MessageName.SURRENDER_VOTING_PASSES].Value, votedNames.Count));

                            if(variables[VariableName.VOTING_SUCCESS_YELL_DURATION].Value > 0)
                                AdminYellAll(String.Format(messages[MessageName.SURRENDER_VOTING_PASSES].Value, votedNames.Count), variables[VariableName.VOTING_SUCCESS_YELL_DURATION].Value);

                            ConsoleWrite("Surrender voting successful with " + votedNames.Count + " votes. Ending round.");

                            new Thread((ThreadStart)delegate { endroundCountdown(winningTeamID); }).Start();

                            resetVoting();
                        }
                        else
                        {
                            int leftTime = variables[VariableName.TIMEOUT].Value - (int)(DateTime.Now - surrenderVotingStart).TotalSeconds;

                            if(variables[VariableName.SURRENDER_VOTING_STATS_INTERVAL].Value == 0)
                                AdminSayTeam(String.Format(messages[MessageName.SURRENDER_VOTING_STATS].Value, votedNames.Count, votesNeeded, formatTime(leftTime)), surrenderingTeamID);
                            else
                                AdminSayPlayer(String.Format(messages[MessageName.SURRENDER_VOTE_PLAYER_MESSAGE].Value, votedNames.Count, votesNeeded, formatTime(leftTime), speaker), speaker);

                            ConsoleWrite("Surrender voting: " + votedNames.Count + "/" + votesNeeded + ". " + formatTime(leftTime) + " left to vote!");
                        }
                    }
                }
            }
        }

        private string formatTime(int seconds)
        {
            if (seconds < 0)
            {
                ConsoleError("formatTime: SECONDS IS NEGATIVE");
                return "";
            }

            int minutes = seconds / 60;
            seconds %= 60;
            return (minutes > 0 ? minutes + " minute" + (minutes == 0 || minutes > 1 ? "s" : "") : "") + (seconds > 0 || minutes == 0 ? (minutes > 0 ? " and " : "") + seconds + " second" + (seconds == 0 || seconds > 1 ? "s" : "") : "");
        }

        private void timeoutCountdown()
        {
            double timeout = variables[VariableName.TIMEOUT].Value;

            DateTime start = DateTime.Now;
            do
            {
                try
                {
                    int interval = variables[VariableName.SURRENDER_VOTING_STATS_INTERVAL].Value;

                    if (interval == 0)
                        Thread.Sleep((int)Math.Round(timeout * 1000));
                    else
                    {
                        Thread.Sleep((int)Math.Round(Math.Min(timeout, interval) * 1000));

                        int leftTime = variables[VariableName.TIMEOUT].Value - (int)(DateTime.Now - surrenderVotingStart).TotalSeconds;
                        if(leftTime > interval)
                            AdminSayTeam(String.Format(messages[MessageName.SURRENDER_VOTING_STATS].Value, votedNames.Count, votesNeeded, formatTime(leftTime)), surrenderingTeamID);
                    }
                }
                catch
                { }

                DateTime now = DateTime.Now;
                timeout -= (now - start).TotalSeconds;
                start = now;
            } while (timeout >= 0);

            lock (this) {
                if (pluginEnabled && surrenderTimeoutCountdown != null)
                {
                    AdminSayTeam(String.Format(messages[MessageName.VOTING_FAILED].Value, votedNames.Count, votesNeeded), surrenderingTeamID);
                    ConsoleWrite("Surrender voting failed! " + votedNames.Count + "/" + votesNeeded + " votes were cast.");
                    resetVoting();
                }
            }
        }

        private void endroundCountdown(int winningTeamID)
        {
            double delay = variables[VariableName.DELAY_ENDROUND].Value;
            do
            {
                DateTime start = DateTime.Now;

                try
                {
                    Thread.Sleep((int)Math.Round(delay * 1000));
                }
                catch
                { }

                delay -= (DateTime.Now - start).TotalSeconds;
            } while (delay >= 0);

            this.ExecuteCommand("procon.protected.send", "mapList.endRound", string.Concat(winningTeamID));
        }

        private void resetVoting()
        {
            lock (this)
            {
                surrenderTimeoutCountdown = null;
                surrenderingTeamID = -1;
                votedNames = null;
                votesNeeded = 0;

                if (surrenderTimeoutCountdown != null)
                    surrenderTimeoutCountdown.Abort();
            }
        }

        public override void OnLevelLoaded(string mapFileName, string GameMode, int roundsPlayed, int roundsTotal)
        {
            resetVoting();

            startTicketCount = -1;

            isConquest = serverInfo.GameMode.ToLower().Contains("conquest");

            ConsoleDebug("Level loaded! Is conquest? " + isConquest);
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
            resetVoting();

            ConsoleDebug("Level ending!");
        }

        public override void OnServerInfo(CServerInfo serverInfo)
        {
            this.serverInfo = serverInfo;

            if (startTicketCount == -1)
            {
                resetVoting();

                isConquest = serverInfo.GameMode.ToLower().Contains("conquest");

                startTicketCount = (serverInfo.TeamScores[0].Score + serverInfo.TeamScores[1].Score) / 2;
                roundStartTime = DateTime.Now;

                ConsoleDebug("Start Ticket Count initially set: " + startTicketCount);
            }
        }
    }
}
