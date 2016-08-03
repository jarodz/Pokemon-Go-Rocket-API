using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using POGOProtos.Inventory.Item;
using POGOProtos.Data;
using POGOProtos.Enums;
using POGOProtos.Settings.Master;
using System;
using POGOProtos.Networking.Responses;
using POGOProtos.Inventory;
using static POGOProtos.Networking.Responses.DownloadItemTemplatesResponse.Types;

namespace PokemonGo.RocketAPI.Logic
{
    public class Inventory
    {
        private readonly Client _client;
        private List<InventoryItem> _inventoryItems;
        private List<PokemonSettings> _pokemonSettings;

        public Inventory(Client client)
        {
            _client = client;
           
            //Task.Run(() => Initialize());
        }
        
        public async Task Initialize()
        {
            _inventoryItems = new List<InventoryItem>();
            _pokemonSettings = new List<PokemonSettings>();
            var inventoryItems = await _client.Inventory.GetInventory();
            foreach (InventoryItem item in inventoryItems.InventoryDelta.InventoryItems)
            {
                _inventoryItems.Add(item);
            }

            DownloadItemTemplatesResponse downloadItemTemplatesResponse = await _client.Download.GetItemTemplates();
            foreach (ItemTemplate itemTemplate in downloadItemTemplatesResponse.ItemTemplates)
            {
                if (itemTemplate.PokemonSettings != null)
                {

                    _pokemonSettings.Add(itemTemplate.PokemonSettings);
                }
            }
        }

        public IEnumerable<PokemonData> GetPokemons()
        {
            //var inventory = await _client.Inventory.GetInventory();
            return
                _inventoryItems.Select(i => i.InventoryItemData?.PokemonData)
                    .Where(p => p != null && p.PokemonId != PokemonId.Missingno);
        }

        public PokemonFamilyId getPokemonFamilyIdForPokemon(PokemonId pokemonId)
        {
            //PokemonId pokemonId = (PokemonId)Enum.Parse(typeof(PokemonId), pokemonIdString, true);
            var pokemonFamilies = Enum.GetValues(typeof(PokemonFamilyId));
            int result = -1;
            foreach (PokemonFamilyId familyId in pokemonFamilies)
            {
                if ((int)(familyId) <= (int)pokemonId)
                {
                    result = (int)familyId;
                }
            }

            return (PokemonFamilyId)result;
        }

        public Candy getCandyItemForPokemonFamilyId(PokemonFamilyId pokemonFamilyId)
        {
            Candy[] candyInventoryItems = _inventoryItems.Select(i => (i.InventoryItemData.Candy)).Where(i => i != null).ToArray();

            foreach (Candy candyItem in candyInventoryItems)
            {
                if (candyItem.FamilyId == pokemonFamilyId)
                {
                    return candyItem;
                }
            }

            return null;
        }

        public int getCandyAmountForPokemonFamily(PokemonFamilyId pokemonFamilyId)
        {
            Candy candyItemForFamily = getCandyItemForPokemonFamilyId(pokemonFamilyId);
            if (candyItemForFamily != null)
            {
                return candyItemForFamily.Candy_;
            }
            return 0;
        }

        public int getCandyAmountForPokemon(PokemonData pokemon)
        {
            PokemonFamilyId familyId = getPokemonFamilyIdForPokemon(pokemon.PokemonId);
            return getCandyAmountForPokemonFamily(familyId);
        }


        //public async Task<IEnumerable<PokemonSettings>> GetPokemonSettings()
        //{
        //    var templates = await _client.Download.GetItemTemplates();
        //    return
        //        templates.ItemTemplates.Select(i => i.PokemonSettings)
        //            .Where(p => p != null && p?.FamilyId != PokemonFamilyId.FamilyUnset);
        //} 


        public IEnumerable<PokemonData> GetDuplicatePokemonToTransfer(bool keepPokemonsThatCanEvolve = false)
        {
            var myPokemon = GetPokemons();

            var pokemonList = myPokemon as IList<PokemonData> ?? myPokemon.ToList();
            var inventory = _inventoryItems;

            if (keepPokemonsThatCanEvolve)
            {
                var results = new List<PokemonData>();
                var pokemonsThatCanBeTransfered = pokemonList.GroupBy(p => p.PokemonId)
                    .Where(x => x.Count() > 1).ToList();

                //var myPokemonFamilies = await GetPokemonFamilies();
                //var pokemonFamilies = myPokemonFamilies.ToArray();

                foreach (var pokemon in pokemonsThatCanBeTransfered)
                {
                    var settings = _pokemonSettings.Single(x => x.PokemonId == pokemon.First().PokemonId);
                    int familyCandy = getCandyAmountForPokemon(pokemon.First());

                    int amountToSkip;
                    if (settings.CandyToEvolve == 0)
                    {
                        amountToSkip = 5; // Keeping n pokemon of the same type that cannot be evolved (i.e. max)
                        //continue
                    } else
                    {
                        amountToSkip = (familyCandy + settings.CandyToEvolve - 1) / settings.CandyToEvolve;
                    }


                    results.AddRange(pokemonList.Where(x => x.PokemonId == pokemon.First().PokemonId && x.Favorite == 0)
                        .OrderByDescending(x => x.Cp)
                        .ThenBy(n => n.StaminaMax)
                        .Skip(amountToSkip)
                        .ToList());

                }

                return results;
            }

            return pokemonList
                .GroupBy(p => p.PokemonId)
                .Where(x => x.Count() > 1)
                .SelectMany(p => p.Where(x => x.Favorite == 0).OrderByDescending(x => x.Cp).ThenBy(n => n.StaminaMax).Skip(1).ToList());
        }


        public IEnumerable<PokemonData> GetPokemonToEvolve()
        {
            var myPokemons = GetPokemons();
            var pokemons = myPokemons.ToList();

            var inventory = _inventoryItems;

            var pokemonToEvolve = new List<PokemonData>();
            foreach (var pokemon in pokemons)
            {
                var settings = _pokemonSettings.Single(x => x.PokemonId == pokemon.PokemonId);
                var familyCandy = getCandyAmountForPokemon(pokemon);

                //Don't evolve if we can't evolve it
                if (settings.EvolutionIds.Count == 0)
                    continue;

                var pokemonCandyNeededAlready = pokemonToEvolve.Count(p => _pokemonSettings.Single(x => x.PokemonId == p.PokemonId).FamilyId == settings.FamilyId) * settings.CandyToEvolve;
                if (familyCandy - pokemonCandyNeededAlready > settings.CandyToEvolve)
                {
                    IncreaseCandyAmountForPokemonId(pokemon.PokemonId, -settings.CandyToEvolve);
                    pokemonToEvolve.Add(pokemon);
                }

            }

            return pokemonToEvolve;
        }

        public IEnumerable<ItemData> GetItems()
        {
            var inventory = _inventoryItems;
            return inventory.Select(i => i.InventoryItemData?.Item).Where(p => p != null);
        }

        public int GetItemAmountByType(ItemId type)
        {
            var items = _inventoryItems;
            foreach (InventoryItem item in items)
            {
                if (item.InventoryItemData.Item != null && item.InventoryItemData.Item.ItemId == type)
                {
                    return item.InventoryItemData.Item.Count;
                }
            }

            return 0;
        }

        public ItemData GetItemDataByItemId(ItemId itemId)
        {
            return _inventoryItems.Select(i => i.InventoryItemData)
                .Where(i => i.Item != null).Select(it => it.Item).Where(it => it.ItemId == itemId).FirstOrDefault();
        }

        public Boolean SetItemIdCount(ItemId itemId, int newCount)
        {
            ItemData itemData = GetItemDataByItemId(itemId);
            if (itemData == null)
            {
                return false;
            }
            itemData.Count = newCount;

            return true;
        }

        public void AddPokemonToInventory(PokemonData pokemonData)
        {
            InventoryItem pokemonInventoryItem = new InventoryItem
            {
                InventoryItemData = new InventoryItemData
                {
                    PokemonData = pokemonData
                }
            };

            _inventoryItems.Add(pokemonInventoryItem);
        }

        public void RemovePokemonFromInventory(PokemonData pokemonData)
        {
            InventoryItem pokemonInventoryItem = _inventoryItems.Select(i => i).Where(i => i.InventoryItemData.PokemonData != null && i.InventoryItemData.PokemonData == pokemonData).First();
            _inventoryItems.Remove(pokemonInventoryItem);
        }

        public bool IncreaseItemAmountInInventory(ItemId itemId, int amount)
        {
            ItemData itemData = GetItemDataByItemId(itemId);
            if (itemData == null)
            {
                return false;
            }
            itemData.Count += amount;

            return true;
        }

        public bool IncreaseCandyAmountForPokemonId(PokemonId pokemonId, int amount)
        {
            PokemonFamilyId pokemonFamilyId = getPokemonFamilyIdForPokemon(pokemonId);
            Candy candyItemForFamily = getCandyItemForPokemonFamilyId(pokemonFamilyId);
            if (candyItemForFamily != null)
            {
                candyItemForFamily.Candy_ += amount;
                return true;
            }

            return false;
        }

        public List<ItemData> GetItemsToRecycle(ISettings settings)
        {
            List<ItemData> result = new List<ItemData>();

            foreach (KeyValuePair<ItemId, int> recycleFilterItem in settings.itemRecycleFilter)
            {
                int itemAmountForType = GetItemAmountByType(recycleFilterItem.Key);
                if (itemAmountForType > recycleFilterItem.Value)
                {
                    result.Add(new ItemData
                    {
                        ItemId = recycleFilterItem.Key,
                        Count = itemAmountForType - recycleFilterItem.Value
                    });
                }
            }
            return result;
        }
    }
}
