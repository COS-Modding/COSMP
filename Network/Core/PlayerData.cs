using LiteNetLib.Utils;
using System.Collections;
using System.Linq;
using UnityEngine;

namespace COSMP.Network.Core
{
    internal class PlayerData
    {
        internal short Id;
        internal string Username;
        internal short Ping;
        internal PositionData Position;
        internal Vector3Data Look;
        internal HumanoidActionType Action;
    }

    internal struct PlayerMeta : INetSerializable
    {
        internal short Id;
        internal string Username;
        internal PositionData Position;

        public readonly void Serialize(NetDataWriter writer)
        {
            writer.Put(Id);
            writer.Put(Username);
            writer.Put(Position);
        }

        public void Deserialize(NetDataReader reader)
        {
            Id = reader.GetShort();
            Username = reader.GetString();
            Position = reader.Get<PositionData>();
        }
    }

    internal struct PlayerList : INetSerializable
    {
        internal PlayerMeta[] List;

        public PlayerList(PlayerData[] players) : this() { List = [.. players.Select(d => new PlayerMeta { Id = d.Id, Username = d.Username, Position = d.Position })]; }

        public readonly void Serialize(NetDataWriter writer)
        {
            writer.Put(List.Length);
            foreach (PlayerMeta meta in List)
            {
                writer.Put(meta);
            }
        }

        public void Deserialize(NetDataReader reader)
        {
            int size = reader.GetInt();
            List = new PlayerMeta[size];
            for (int i = 0; i < size; i++)
            {
                List[i] = reader.Get<PlayerMeta>();
            }
        }

        public readonly IEnumerator GetEnumerator() => List.GetEnumerator();
    }

    internal struct PositionData : INetSerializable
    {
        internal string Place;
        internal bool Run;
        internal Vector3Data Position;
        internal Vector3Data Destination;

        public readonly void Serialize(NetDataWriter writer)
        {
            writer.Put(Place);
            writer.Put(Run);
            writer.Put(Position);
            writer.Put(Destination);
        }

        public void Deserialize(NetDataReader reader)
        {
            Place = reader.GetString();
            Run = reader.GetBool();
            Position = reader.Get<Vector3Data>();
            Destination = reader.Get<Vector3Data>();
        }

        public readonly override string ToString() => $"{Place}[{Position};{Destination}]";
    }

    internal struct Vector3Data : INetSerializable
    {
        internal float X;
        internal float Y;
        internal float Z;

        public readonly void Serialize(NetDataWriter writer)
        {
            writer.Put(X);
            writer.Put(Y);
            writer.Put(Z);
        }

        public void Deserialize(NetDataReader reader)
        {
            X = reader.GetFloat();
            Y = reader.GetFloat();
            Z = reader.GetFloat();
        }

        public readonly override string ToString() => $"({X};{Y};{Z})";

        public override readonly bool Equals(object obj)
        {
            if (obj is Vector3Data data) return X == data.X && Y == data.Y && Z == data.Z;
            if (obj is Vector3 vector) return X == vector.x && Y == vector.y && Z == vector.z;
            return false;
        }
        public override readonly int GetHashCode() => (X, Y, Z).GetHashCode();

        public static implicit operator Vector3Data(Vector3 vector) => new() { X = vector.x, Y = vector.y, Z = vector.z };
        public static implicit operator Vector3(Vector3Data data) => new(data.X, data.Y, data.Z);
    }

    internal enum PlayerErrorCode
    {
        Success,
        UsernameTaken,
        Ban
    }
}
