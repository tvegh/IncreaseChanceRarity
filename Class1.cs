using System;
using BepInEx;
using BepInEx.Configuration;
using R2API.Utils;
using R2API;
using RoR2;
using UnityEngine.Networking;
using UnityEngine;



//DEFAULT VALUES
//failureWeight = 10.1
//tier1Weight = 8
//tier2Weight = 2
//tier3Weight = 0.2
//equipmentWeight = 2



namespace Increase_Rarity
{
	[BepInDependency("com.bepis.r2api")]
	[BepInPlugin("com.TeaBoneJones.IncreaseRarity", "Increase Chance Rarity", "1.0.0")]
	public class IncreaseRarity : BaseUnityPlugin
	{
        private static ConfigWrapper<bool> noRefresh;
        private static ConfigWrapper<int> maximumPurchase;
        private static ConfigWrapper<bool> chatMessages;
        [Server]
        public void Awake()
		{
            noRefresh = Config.Wrap("Shrine of Chance", "NoCooldown", "When enabled, this gets rid of the 2 second cooldown between purchases. Default = false", false);
            maximumPurchase = Config.Wrap("Shrine of Chance", "MaxPurchase", "The maximum number of purchases you can make before the shrine is disabled. Default = 2", 2);
            chatMessages = Config.Wrap("Chat", "ChatMessages", "When enabled, you will get a message in the chat letting you know you have increased odds. Default = true", true);

            //init failure count
            int failureCount = 0;

            On.RoR2.ShrineChanceBehavior.AddShrineStack += (orig, self, activator) =>
            {
                //set maximum purchase count
                self.maxPurchaseCount = maximumPurchase.Value;

                /* 
                everything is default the first 2 tries.
                if you fail 2 times in a row, on the third try, the weights change to be more favorable for a tier 2 item or equipment
                if you fail 5 times in a row, on the sixth try, the weights change to be more favorable for a tier 3 item
                if you fail 8 times in a row, on the ninth try, you are guaranteed a tier 3 item
                */

                if (failureCount >= 0 && failureCount < 2)
                {
                    self.tier1Weight = 8;
                    self.tier2Weight = 2;
                    self.tier3Weight = 0.2f;
                    self.equipmentWeight = 2;
                    self.failureWeight = 10.1f;
                }
                else if (failureCount >= 2 && failureCount < 5)
                {
                    if (chatMessages.Value)
                    {
                        Chat.AddMessage("Increased chances for tier 2 item or equipment");
                    }
                    self.tier1Weight = -100;
                    self.tier2Weight = 4;
                    self.tier3Weight = 0.2f;
                    self.equipmentWeight = 2;
                    self.failureWeight = 10.1f;
                }
                else if (failureCount >= 5 && failureCount < 8)
                {
                    if (chatMessages.Value)
                    {
                        Chat.AddMessage("Increased chances for tier 3 item");
                    }
                    self.tier1Weight = -100;
                    self.tier2Weight = 2;
                    self.tier3Weight = 4;
                    self.equipmentWeight = -100;
                    self.failureWeight = 10.1f;
                }
                else if (failureCount >= 8)
                {
                    if (chatMessages.Value)
                    {
                        Chat.AddMessage("Guaranteed Legendary. You've earned it.");
                    }
                    self.tier1Weight = -100;
                    self.tier2Weight = -100;
                    self.tier3Weight = 1000;
                    self.equipmentWeight = -100;
                    self.failureWeight = -100;
                }

                if (!NetworkServer.active)
                {
                    Debug.LogWarning("[Server] function 'System.Void RoR2.ShrineChanceBehavior::AddShrineStack(RoR2.Interactor)' called on client");
                    return;
                }
                Xoroshiro128Plus rng = self.GetFieldValue<Xoroshiro128Plus>("rng");
                PickupIndex none = PickupIndex.none;
                PickupIndex value = rng.NextElementUniform<PickupIndex>(Run.instance.availableTier1DropList);
                PickupIndex value2 = rng.NextElementUniform<PickupIndex>(Run.instance.availableTier2DropList);
                PickupIndex value3 = rng.NextElementUniform<PickupIndex>(Run.instance.availableTier3DropList);
                PickupIndex value4 = rng.NextElementUniform<PickupIndex>(Run.instance.availableEquipmentDropList);
                WeightedSelection<PickupIndex> weightedSelection = new WeightedSelection<PickupIndex>(8);
                weightedSelection.AddChoice(none, self.failureWeight);
                weightedSelection.AddChoice(value, self.tier1Weight);
                weightedSelection.AddChoice(value2, self.tier2Weight);
                weightedSelection.AddChoice(value3, self.tier3Weight);
                weightedSelection.AddChoice(value4, self.equipmentWeight);
                PickupIndex pickupIndex = weightedSelection.Evaluate(rng.nextNormalizedFloat);
                bool flag = pickupIndex == PickupIndex.none;
                if (flag)
                {
                    Chat.SubjectFormatChatMessage formatChatMessage = new Chat.SubjectFormatChatMessage();
                    formatChatMessage.subjectAsCharacterBody = activator.GetComponent<CharacterBody>();
                    formatChatMessage.baseToken = "SHRINE_CHANCE_FAIL_MESSAGE";
                    Chat.SendBroadcastChat((Chat.ChatMessageBase) formatChatMessage);
                    //on failure, increment failure count
                    failureCount++;
                }
                else
                {
                    self.SetFieldValue<int>("successfulPurchaseCount", self.GetFieldValue<int>("successfulPurchaseCount") + 1);
                    //this.successfulPurchaseCount++;
                    PickupDropletController.CreatePickupDroplet(pickupIndex, self.dropletOrigin.position, self.dropletOrigin.forward * 20f);
                    Chat.SubjectFormatChatMessage formatChatMessage = new Chat.SubjectFormatChatMessage();
                    formatChatMessage.subjectAsCharacterBody = activator.GetComponent<CharacterBody>();
                    formatChatMessage.baseToken = "SHRINE_CHANCE_SUCCESS_MESSAGE";
                    Chat.SendBroadcastChat((Chat.ChatMessageBase) formatChatMessage);
                    //on success, reset failure count to 0
                    failureCount = 0;
                }
                //Action<bool, Interactor> action = ShrineChanceBehavior.onShrineChancePurchaseGlobal;
                Action<bool, Interactor> action = typeof(ShrineChanceBehavior).GetFieldValue<Action<bool, Interactor>>("onShrineChancePurchaseGlobal");
                if (action != null)
                {
                    action(flag, activator);
                }
                self.SetFieldValue<bool>("waitingForRefresh", true);
                //this.waitingForRefresh = true;
                if (noRefresh.Value)
                {
                    self.SetFieldValue<float>("refreshTimer", 0f);
                }
                else
                {
                    self.SetFieldValue<float>("refreshTimer", 2f);
                }
                //this.refreshTimer = 2f;
                EffectManager.instance.SpawnEffect(Resources.Load<GameObject>("Prefabs/Effects/ShrineUseEffect"), new EffectData
                {
                    origin = base.transform.position,
                    rotation = Quaternion.identity,
                    scale = 1f,
                    color = self.shrineColor
                }, true);
                if (self.GetFieldValue<int>("successfulPurchaseCount") >= self.maxPurchaseCount)
                {
                    self.symbolTransform.gameObject.SetActive(false);
                }



            };
		}
	}
}
