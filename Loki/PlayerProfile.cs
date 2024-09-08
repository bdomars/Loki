using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Windows;

namespace Loki
{
    /// <summary>
    /// Player profile as stored in a '.fch' file.
    /// </summary>
    public class PlayerProfile
    {
        public long PlayerId { get; set; }
        public string PlayerName { get; set; }
        public string StartSeed { get; set; }
        public DateTime DateCreated { get; set; }
        public bool UsedCheats { get; set; }
        public Dictionary<string, float> KnownWorlds { get; set; }
        public Dictionary<string, float> KnownWorldKeys { get; set; }
        public Dictionary<string, float> KnownCommands { get; set; }
        public PlayerStats Stats { get; private set; }
        public bool FirstSpawn { get; private set; }
        public Player Player { get; private set; }
        private List<(long, WorldPlayerData)> _worldData;
        private PlayerProfile() { }

        public static PlayerProfile Read(Stream input, bool leaveOpen = false)
        {
            using var reader = new BinaryReader(input, Encoding.UTF8, leaveOpen);
            var totalExpectedSize = reader.ReadInt32();

            var startPosition = input.Position;

            var version = reader.ReadInt32();

            if (!Version.IsProfileCompatible(version))
                throw new InvalidDataException("Character version is not compatible");

            var playerStats = new PlayerStats();

            // ToDo: if version != 39 "create backup" (just a note: this is what Valheim does, maybe what we want in Loki)
            if (version >= 38)
            {
                int statsCount = reader.ReadInt32();
                for (int i = 0; i < statsCount; i++)
                {
                    playerStats[(PlayerStatType)i] = reader.ReadSingle();                    
                }
            }
            else if (version >= 28)
            {
                playerStats[PlayerStatType.EnemyKills] = reader.ReadInt32();
                playerStats[PlayerStatType.Deaths] = reader.ReadInt32();
                playerStats[PlayerStatType.CraftsOrUpgrades] = reader.ReadInt32();
                playerStats[PlayerStatType.Builds] = reader.ReadInt32();
            }

            bool firstSpawn = false;
            if (version >= 40)
            {
                firstSpawn = reader.ReadBoolean();
            }

            int worldCount = reader.ReadInt32();
            var worldData = new List<(long, WorldPlayerData)>();
            for (var i = 0; i < worldCount; i++)
                worldData.Add((
                    reader.ReadInt64(),
                    new WorldPlayerData
                    {
                        HaveCustomSpawn = reader.ReadBoolean(),
                        SpawnPoint = reader.ReadVector3(),
                        HaveLogoutPoint = reader.ReadBoolean(),
                        LogoutPoint = reader.ReadVector3(),
                        HaveDeathPoint = version >= 30 && reader.ReadBoolean(),
                        DeathPoint = version >= 30 ? reader.ReadVector3() : default,
                        HomePoint = reader.ReadVector3(),
                        MapData = version >= 29 && reader.ReadBoolean() ? reader.ReadByteArray() : default,
                    }));

            string playerName = reader.ReadString(); // Used later
            long playerId = reader.ReadInt64(); // Used later
            string startSeed = reader.ReadString(); // Used later

            bool usedCheats = default;
            var knownWorlds = new Dictionary<string, float>();
            var knownWorldKeys = new Dictionary<string, float>();
            var knownCommands = new Dictionary<string, float>();
            DateTime dateCreated;

            if (version >= 38)
            {
                usedCheats = reader.ReadBoolean();
                dateCreated = DateTimeOffset.FromUnixTimeSeconds(reader.ReadInt64()).Date;
                int knownWorldsCount = reader.ReadInt32();

                for (int i = 0; i < knownWorldsCount; i++)
                {
                    knownWorlds[reader.ReadString()] = reader.ReadSingle();
                }
                var knownWorldKeysCount = reader.ReadInt32();

                for (int i = 0; i < knownWorldKeysCount; i++)
                {
                    knownWorldKeys[reader.ReadString()] = reader.ReadSingle();
                }
                var knownCommandsCount = reader.ReadInt32();

                for (int i = 0; i < knownCommandsCount; i++)
                {
                    knownCommands[reader.ReadString()] = reader.ReadSingle();
                }
            }
            else
            {
                dateCreated = new DateTime(2021, 2, 2);
            }
            var player = reader.ReadBoolean() ? Player.Read(input, true) : default;

            // Verify we read all the data and haven't skipped anything.
            var dataRead = input.Position - startPosition;
            if (dataRead != totalExpectedSize)
            {
                var message =
                    "Amount of data read did not match the length header. " +
                    $"Data read = {dataRead}B, Length in header = {totalExpectedSize}B";

                var result = MessageBox.Show(
                    $"{message}" + Environment.NewLine + Environment.NewLine +
                    "Do you want to continue loading? (Not Recommended)",
                    $"Error reading {nameof(PlayerProfile)}",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning);

                if (result == MessageBoxResult.No)
                {
                    throw new InvalidDataException(message);
                }
            }

            return new PlayerProfile
            {
                Stats = playerStats,
                PlayerId = playerId,
                PlayerName = playerName,
                StartSeed = startSeed,
                DateCreated = dateCreated,
                UsedCheats = usedCheats,
                KnownWorlds = knownWorlds,
                KnownWorldKeys = knownWorldKeys,
                KnownCommands = knownCommands,
                _worldData = worldData,
                Player = player,
                FirstSpawn = firstSpawn,
            };
        }

        public void Write(Stream output, bool leaveOpen = false)
        {
            if (!output.CanSeek)
                throw new ArgumentException("Output stream must support seek");

            const int profileOffset = 4;
            output.Position = profileOffset;

            using var writer = new BinaryWriter(output, Encoding.UTF8, leaveOpen);

            writer.Write(Version.ProfileVersion);

            writer.Write(Stats.Count);
            for (int i = 0; i < Stats.Count; i++)
            {
                writer.Write(Stats[(PlayerStatType)i]);
            }

            writer.Write(FirstSpawn);

            writer.Write(_worldData.Count);
            foreach (var (key, worldData) in _worldData)
            {
                writer.Write(key);
                writer.Write(worldData.HaveCustomSpawn);
                writer.Write(worldData.SpawnPoint);
                writer.Write(worldData.HaveLogoutPoint);
                writer.Write(worldData.LogoutPoint);
                writer.Write(worldData.HaveDeathPoint);
                writer.Write(worldData.DeathPoint);
                writer.Write(worldData.HomePoint);
                writer.Write(worldData.MapData != null);
                if (worldData.MapData != null) writer.WriteByteArray(worldData.MapData);
            }

            writer.Write(PlayerName);
            writer.Write(PlayerId);
            writer.Write(StartSeed);
            writer.Write(UsedCheats);
            writer.Write(new DateTimeOffset(DateCreated).ToUnixTimeSeconds());

            writer.Write(KnownWorlds.Count);
            foreach (KeyValuePair<string, float> knownWorld in KnownWorlds)
            {
                writer.Write(knownWorld.Key);
                writer.Write(knownWorld.Value);
            }

            writer.Write(KnownWorldKeys.Count);
            foreach (KeyValuePair<string, float> knownWorldKey in KnownWorldKeys)
            {
                writer.Write(knownWorldKey.Key);
                writer.Write(knownWorldKey.Value);
            }

            writer.Write(KnownCommands.Count);
            foreach (KeyValuePair<string, float> knownCommand in KnownCommands)
            {
                writer.Write(knownCommand.Key);
                writer.Write(knownCommand.Value);
            }

            writer.Write(Player != null);
            if (Player != null)
            {
                output.Position += 4;
                var playerOffset = output.Position;
                Player.Write(output, true);

                // Write size of player data before it.
                int playerDataSize = (int)(output.Position - playerOffset);
                output.Position = playerOffset - 4;
                writer.Write(playerDataSize);
                output.Position += playerDataSize;
            }

            // Length of entire profile is written to very start.
            int profileLength = (int)(output.Position - profileOffset);
            output.Position = 0;
            writer.Write(profileLength);

            // Write tail of file (SHA512 hash, length prefixed)
            output.Position = profileOffset;
            var hash = SHA512.Create().ComputeHash(output);
            writer.WriteByteArray(hash);
        }
    }
}
