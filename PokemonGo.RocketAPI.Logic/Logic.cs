using System;
using System.Linq;
using System.Threading.Tasks;
using PokemonGo.RocketAPI.Exceptions;
using PokemonGo.RocketAPI.Extensions;
using PokemonGo.RocketAPI.Logic.Utils;
using POGOProtos.Map.Fort;
using POGOProtos.Networking.Responses;
using POGOProtos.Map.Pokemon;
using POGOProtos.Inventory.Item;
using PokemonGo.RocketAPI.Enums;
using POGOProtos.Data;
using POGOProtos.Data.Capture;

namespace PokemonGo.RocketAPI.Logic
{
    public class Logic
    {
        private readonly Client _client;
        private readonly ISettings _clientSettings;
        private readonly Inventory _inventory;

        public Logic(ISettings clientSettings)
        {
            _clientSettings = clientSettings;
            _client = new Client(_clientSettings);
            _inventory = new Inventory(_client);
        }

        public async void Execute()
        {
			Logger.Write($"Starting Execute on login server: {_clientSettings.AuthType} ({_clientSettings.PtcUsername})", LogLevel.Info);

            while (true)
            {
                try
                {
					if (_clientSettings.AuthType == AuthType.Ptc)
					{
						Logger.Write("Logging in using pokemon trainer account");
						await _client.Login.DoPtcLogin(_clientSettings.PtcUsername, _clientSettings.PtcPassword);
						Logger.Write("Logged in!");
					}
                     
                    //else if (_clientSettings.AuthType == AuthType.Google)
                    //    await _client.Login.DoGoogleLogin(_clientSettings.GoogleUsername, _clientSettings.GooglePassword);

                    await PostLoginExecute();
                }
                catch (AccessTokenExpiredException)
                {
                    Logger.Write($"Access token expired", LogLevel.Info);
                }
                catch (PtcOfflineException)
                {
                    Logger.Write("PTC seems to be offline", LogLevel.Info);
                }
                catch (Exception e)
                {
                    Logger.Write("Unhandled exception: " + e.Message);
                }
                await Task.Delay(10000);
            }
        }

        public async Task PostLoginExecute()
        {
            await Task.Delay(1500);
            await _inventory.Initialize(); // Get the inventory, use it locally henceforth
            while (true)
            {
                try
                {
                    await EvolveAllPokemonWithEnoughCandy();
                    await TransferDuplicatePokemon(true);
                    await RecycleItems();
                    await ExecuteFarmingPokestopsAndPokemons();
                }
                catch (AccessTokenExpiredException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Logger.Write($"Exception: {ex}", LogLevel.Error);
                }

                await Task.Delay(10000);
            }
        }

        public async Task RepeatAction(int repeat, Func<Task> action)
        {
            for (int i = 0; i < repeat; i++)
                await action();
        }

        private async Task ExecuteFarmingPokestopsAndPokemons()
        {
            var mapObjects = await _client.Map.GetMapObjects();

            var pokeStops = mapObjects.MapCells.SelectMany(i => i.Forts).Where(i => i.Type == FortType.Checkpoint && i.CooldownCompleteTimestampMs < DateTime.UtcNow.ToUnixTime());

            foreach (var pokeStop in pokeStops)
            {
                var update = await _client.Player.UpdatePlayerLocation(pokeStop.Latitude, pokeStop.Longitude, -2);
                //var fortInfo = await client.GetFort(pokeStop.Id, pokeStop.Latitude, pokeStop.Longitude);
                var fortSearch = await _client.Fort.SearchFort(pokeStop.Id, pokeStop.Latitude, pokeStop.Longitude);
                UpdateItemCountFromFortSearch(fortSearch);
                Logger.Write($"PokeStop xp: {fortSearch.ExperienceAwarded}, gems: { fortSearch.GemsAwarded}, items: {StringUtils.GetSummedFriendlyNameOfItemAwardList(fortSearch.ItemsAwarded)}", LogLevel.Info);
                await Task.Delay(15000);
                await RecycleItems();
                await ExecuteCatchAllNearbyPokemons();
                await TransferDuplicatePokemon(true);
            }
        }

        private void UpdateItemCountFromFortSearch(FortSearchResponse fortSearch)
        {
            foreach (ItemAward awardedItem in fortSearch.ItemsAwarded) {
                _inventory.SetItemIdCount(awardedItem.ItemId, awardedItem.ItemCount);
            }
        }

        private async Task ExecuteCatchAllNearbyPokemons()
        {
            var mapObjects = await _client.Map.GetMapObjects();

            var pokemons = mapObjects.MapCells.SelectMany(i => i.CatchablePokemons);

            foreach (var pokemon in pokemons)
            {
                await _client.Player.UpdatePlayerLocation(pokemon.Latitude, pokemon.Longitude, -2);

                var encounter = await _client.Encounter.EncounterPokemon(pokemon.EncounterId, pokemon.SpawnPointId);
                await CatchEncounter(encounter, pokemon);
                await Task.Delay(15000);
            }
        }

        private string getPokemonEncounterString(EncounterResponse encounter)
        {
            PokemonData pokemonData = encounter.WildPokemon.PokemonData;
            return
                "A wild " + pokemonData.PokemonId + " appeared! " +
                "[CP " + pokemonData.Cp + ", att " + pokemonData.IndividualAttack +
                ", def " + pokemonData.IndividualDefense +
                ", stn " + pokemonData.IndividualStamina + "] - catch prob." + encounter.CaptureProbability.CaptureProbability_;
        }

        private async Task CatchEncounter(EncounterResponse encounter, MapPokemon pokemon)
        {
            Logger.Write(getPokemonEncounterString(encounter), LogLevel.Info);
            CatchPokemonResponse caughtPokemonResponse;
            var pokeballItemId = GetBestBall(encounter);
            int indexOfBallCaptureProbability = encounter.CaptureProbability.PokeballType.IndexOf(pokeballItemId);
            float captureProbabilityForChosenBall = encounter.CaptureProbability.CaptureProbability_[indexOfBallCaptureProbability];
            do
            {
                if (captureProbabilityForChosenBall < 0.4)
                {
                    //Throw berry is we can
                    await UseBerry(pokemon.EncounterId, pokemon.SpawnPointId);
                }
                Logger.Write($"Throwing {pokeballItemId}", LogLevel.Info);
                _inventory.IncreaseItemAmountInInventory(pokeballItemId, -1);
                caughtPokemonResponse = await _client.Encounter.CatchPokemon(pokemon.EncounterId, pokemon.SpawnPointId, pokeballItemId);
                Logger.Write(
                    caughtPokemonResponse.Status == CatchPokemonResponse.Types.CatchStatus.CatchSuccess ? 
                    $"Caught {pokemon.PokemonId} [CP {encounter?.WildPokemon?.PokemonData?.Cp}] using {pokeballItemId}" :
                    $"{pokemon.PokemonId} [CP {encounter?.WildPokemon?.PokemonData?.Cp}] got away while using {pokeballItemId}..", LogLevel.Info
                );
                if (caughtPokemonResponse.Status == CatchPokemonResponse.Types.CatchStatus.CatchSuccess)
                {
                    _inventory.AddPokemonToInventory(encounter.WildPokemon.PokemonData);
                }
                await Task.Delay(2000);
            }
            while (caughtPokemonResponse.Status == CatchPokemonResponse.Types.CatchStatus.CatchMissed);
        }
        
        private async Task EvolveAllPokemonWithEnoughCandy()
        {
            var pokemonToEvolve = _inventory.GetPokemonToEvolve();
            foreach (var pokemon in pokemonToEvolve)
            {
                var evolvePokemonOutProto = await _client.Inventory.EvolvePokemon(pokemon.Id);

                if (evolvePokemonOutProto.Result == EvolvePokemonResponse.Types.Result.Success)
                {
                    Logger.Write($"Evolved {pokemon.PokemonId} successfully for {evolvePokemonOutProto.ExperienceAwarded}xp", LogLevel.Info);
                    _inventory.RemovePokemonFromInventory(pokemon);
                    _inventory.AddPokemonToInventory(evolvePokemonOutProto.EvolvedPokemonData);
                }
                    
                else
                    Logger.Write($"Failed to evolve {pokemon.PokemonId}. EvolvePokemonOutProto.Result was {evolvePokemonOutProto.Result}, stopping evolving {pokemon.PokemonId}", LogLevel.Info);
                    

                await Task.Delay(3000);
            }
        }

        private async Task TransferDuplicatePokemon(bool keepPokemonsThatCanEvolve = false)
        {
            var duplicatePokemons = await _inventory.GetDuplicatePokemonToTransfer(keepPokemonsThatCanEvolve);

            foreach (var duplicatePokemon in duplicatePokemons)
            {
                var transfer = await _client.Inventory.TransferPokemon(duplicatePokemon.Id);
                _inventory.IncreaseCandyAmountForPokemonId(duplicatePokemon.PokemonId, transfer.CandyAwarded);
                _inventory.RemovePokemonFromInventory(duplicatePokemon);
                Logger.Write($"Transferring {duplicatePokemon.PokemonId} [CP {duplicatePokemon.Cp}]", LogLevel.Info);
                await Task.Delay(500);
            }
        }

        private async Task RecycleItems()
        {
            var items =  _inventory.GetItemsToRecycle(_clientSettings);

            foreach (var item in items)
            {
                var transfer = await _client.Inventory.RecycleItem(item.ItemId, item.Count);
                _inventory.SetItemIdCount(item.ItemId, transfer.NewCount);
                Logger.Write($"Recycled {item.Count}x {item.ItemId}", LogLevel.Info);
                await Task.Delay(500);
            }
        }

        private ItemId GetBestBall(EncounterResponse encounterResponse)
        {
            var pokemonCp = encounterResponse.WildPokemon?.PokemonData?.Cp;
            var pokeBallsCount = _inventory.GetItemAmountByType(ItemId.ItemPokeBall);
            var greatBallsCount = _inventory.GetItemAmountByType(ItemId.ItemGreatBall);
            var ultraBallsCount =  _inventory.GetItemAmountByType(ItemId.ItemUltraBall);
            var masterBallsCount =  _inventory.GetItemAmountByType(ItemId.ItemMasterBall);

            if (masterBallsCount > 0 && pokemonCp >= 1000)
                return ItemId.ItemMasterBall;
            else if (ultraBallsCount > 0 && pokemonCp >= 1000)
                return ItemId.ItemUltraBall;
            else if (greatBallsCount > 0 && pokemonCp >= 1000)
                return ItemId.ItemGreatBall;

            if (ultraBallsCount > 0 && pokemonCp >= 600)
                return ItemId.ItemUltraBall;
            else if (greatBallsCount > 0 && pokemonCp >= 600)
                return ItemId.ItemGreatBall;

            if (greatBallsCount > 0 && pokemonCp >= 350)
                return ItemId.ItemGreatBall;

            if (pokeBallsCount > 0)
                return ItemId.ItemPokeBall;
            if (greatBallsCount > 0)
                return ItemId.ItemGreatBall;
            if (ultraBallsCount > 0)
                return ItemId.ItemUltraBall;
            if (masterBallsCount > 0)
                return ItemId.ItemMasterBall;

            return ItemId.ItemPokeBall;
        }

        public async Task UseBerry(ulong encounterId, string spawnPointId)
        {
            var inventoryBalls = _inventory.GetItems();
            var berries = inventoryBalls.Where(p => p.ItemId == ItemId.ItemRazzBerry);
            var berry = berries.FirstOrDefault();

            if (berry == null)
                return;
            
            var useRaspberry = await _client.Encounter.UseCaptureItem(encounterId, ItemId.ItemRazzBerry, spawnPointId);
            _inventory.IncreaseItemAmountInInventory(ItemId.ItemRazzBerry, -1);
            Logger.Write($"Using a razz berry. Remaining: {berry.Count}", LogLevel.Info);
            await Task.Delay(3000);
        }
    }
}
