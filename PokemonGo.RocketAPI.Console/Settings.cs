using PokemonGo.RocketAPI.Enums;
using System;
using System.Collections.Generic;
using POGOProtos.Inventory.Item;

namespace PokemonGo.RocketAPI.Console
{
    public class Settings : ISettings
	{
		public AuthType AuthType => AuthType.Ptc;
		public string PtcUsername => UserSettings.Default.PtcUsername;
        public string PtcPassword => UserSettings.Default.PtcPassword;
        public double DefaultLatitude => UserSettings.Default.DefaultLatitude;
        public double DefaultLongitude => UserSettings.Default.DefaultLongitude;

        ICollection<KeyValuePair<ItemId, int>> ISettings.itemRecycleFilter
        {
            get
            {
                //Type and amount to keep
                return new[]
                {
                    new KeyValuePair<ItemId, int>(ItemId.ItemPokeBall, 25),
                    new KeyValuePair<ItemId, int>(ItemId.ItemGreatBall, 50),
                    new KeyValuePair<ItemId, int>(ItemId.ItemUltraBall, 75),
                    new KeyValuePair<ItemId, int>(ItemId.ItemPotion, 5),
		            new KeyValuePair<ItemId, int>(ItemId.ItemSuperPotion, 10),
                    new KeyValuePair<ItemId, int>(ItemId.ItemHyperPotion, 20),
                    new KeyValuePair<ItemId, int>(ItemId.ItemMaxPotion, 30),
                    new KeyValuePair<ItemId, int>(ItemId.ItemRevive, 10),
                    new KeyValuePair<ItemId, int>(ItemId.ItemMaxRevive, 25),
		            new KeyValuePair<ItemId, int>(ItemId.ItemRazzBerry, 50)
                };
            }

            set
            {
                throw new NotImplementedException();
            }
        }

        public string GoogleRefreshToken
        {
            get { return UserSettings.Default.GoogleRefreshToken; }
            set
            {
                UserSettings.Default.GoogleRefreshToken = value;
                UserSettings.Default.Save();
            }
        }

        public double DefaultAltitude
        {
            get { return UserSettings.Default.DefaultAltitude; }
        }

        public string GoogleUsername
        {
            get
            {
                return UserSettings.Default.GoogleUsername;
            }
        }

        public string GooglePassword
        {
            get
            {
                return UserSettings.Default.GooglePassword;
            }
        }
    }
}
