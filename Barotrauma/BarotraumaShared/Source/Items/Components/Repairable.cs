﻿using Barotrauma.Networking;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using System;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class Repairable : ItemComponent, IServerSerializable, IClientSerializable
    {
        public static float SkillIncreaseMultiplier = 0.4f;

        private string header;

        private float deteriorationTimer;

        [Serialize(0.0f, true), Editable(MinValueFloat = 0.0f, MaxValueFloat = 100.0f, DecimalCount = 2, ToolTip = "How fast the condition of the item deteriorates per second.")]
        public float DeteriorationSpeed
        {
            get;
            set;
        }

        [Serialize(0.0f, true), Editable(MinValueFloat = 0.0f, MaxValueFloat = 1000.0f, DecimalCount = 2, ToolTip = "Minimum initial delay before the item starts to deteriorate.")]
        public float MinDeteriorationDelay
        {
            get;
            set;
        }

        [Serialize(0.0f, true), Editable(MinValueFloat = 0.0f, MaxValueFloat = 1000.0f, DecimalCount = 2, ToolTip = "Maximum initial delay before the item starts to deteriorate.")]
        public float MaxDeteriorationDelay
        {
            get;
            set;
        }

        [Serialize(50.0f, true), Editable(MinValueFloat = 0.0f, MaxValueFloat = 100.0f, ToolTip = "The item won't deteriorate spontaneously if the condition is below this value. For example, if set to 10, the condition will spontaneously drop to 10 and then stop dropping (unless the item is damaged further by external factors).")]
        public float MinDeteriorationCondition
        {
            get;
            set;
        }

        [Serialize(80.0f, true), Editable(MinValueFloat = 0.0f, MaxValueFloat = 100.0f, ToolTip = "The condition of the item has to be below this before the repair UI becomes usable.")]
        public float ShowRepairUIThreshold
        {
            get;
            set;
        }

        [Serialize(100.0f, true), Editable(MinValueFloat = 0.0f, MaxValueFloat = 100.0f, ToolTip = "The amount of time it takes to fix the item with insufficient skill levels.")]
        public float FixDurationLowSkill
        {
            get;
            set;
        }

        [Serialize(10.0f, true), Editable(MinValueFloat = 0.0f, MaxValueFloat = 100.0f, ToolTip = "The amount of time it takes to fix the item with sufficient skill levels.")]
        public float FixDurationHighSkill
        {
            get;
            set;
        }

        private Character currentFixer;
        public Character CurrentFixer
        {
            get { return currentFixer; }
            set
            {
                if (currentFixer == value || item.IsFullCondition) return;
                if (currentFixer != null) currentFixer.AnimController.Anim = AnimController.Animation.None;
                currentFixer = value;
            }
        }

        public Repairable(Item item, XElement element)
            : base(item, element)
        {
            IsActive = true;
            canBeSelected = true;

            this.item = item;
            header = element.GetAttributeString("name", "");
            InitProjSpecific(element);
        }

        public override void OnItemLoaded()
        {
            deteriorationTimer = Rand.Range(MinDeteriorationDelay, MaxDeteriorationDelay);

#if SERVER
            //let the clients know the initial deterioration delay
            item.CreateServerEvent(this);
#endif
        }

        partial void InitProjSpecific(XElement element);
        
        public void StartRepairing(Character character)
        {
            CurrentFixer = character;
        }

        public override void UpdateBroken(float deltaTime, Camera cam)
        {
            Update(deltaTime, cam);
        }

        public override void Update(float deltaTime, Camera cam)
        {
            UpdateProjSpecific(deltaTime);

            if (CurrentFixer == null)
            {
                if (item.Condition > 0.0f)
                {
                    if (deteriorationTimer > 0.0f)
                    {
                        if (GameMain.NetworkMember == null || !GameMain.NetworkMember.IsClient)
                        {
                            deteriorationTimer -= deltaTime;
#if SERVER
                            if (deteriorationTimer <= 0.0f) { item.CreateServerEvent(this); }
#endif
                        }
                        return;
                    }

                    if (item.Condition > MinDeteriorationCondition)
                    {
                        item.Condition -= DeteriorationSpeed * deltaTime;
                    }
                }
                return;
            }

            if (Item.IsFullCondition || CurrentFixer.SelectedConstruction != item || !currentFixer.CanInteractWith(item))
            {
                currentFixer.AnimController.Anim = AnimController.Animation.None;
                currentFixer = null;
                return;
            }

            UpdateFixAnimation(CurrentFixer);

            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient) { return; }

            float successFactor = requiredSkills.Count == 0 ? 1.0f : 0.0f;
            foreach (Skill skill in requiredSkills)
            {
                float characterSkillLevel = CurrentFixer.GetSkillLevel(skill.Identifier);
                if (characterSkillLevel >= skill.Level) successFactor += 1.0f / requiredSkills.Count;
                CurrentFixer.Info.IncreaseSkillLevel(skill.Identifier,
                    SkillIncreaseMultiplier * deltaTime / Math.Max(characterSkillLevel, 1.0f),
                     CurrentFixer.WorldPosition + Vector2.UnitY * 100.0f);
            }

            bool wasBroken = !item.IsFullCondition;
            float fixDuration = MathHelper.Lerp(FixDurationLowSkill, FixDurationHighSkill, successFactor);
            if (fixDuration <= 0.0f)
            {
                item.Condition = item.MaxCondition;
            }
            else
            {
                item.Condition += deltaTime / (fixDuration / item.MaxCondition);
            }

            if (wasBroken && item.IsFullCondition)
            {
                SteamAchievementManager.OnItemRepaired(item, currentFixer);
                deteriorationTimer = Rand.Range(MinDeteriorationDelay, MaxDeteriorationDelay);
#if SERVER
                item.CreateServerEvent(this);
#endif
            }
        }

        partial void UpdateProjSpecific(float deltaTime);

        private void UpdateFixAnimation(Character character)
        {
            character.AnimController.UpdateUseItem(false, item.WorldPosition + new Vector2(0.0f, 100.0f) * ((item.Condition / item.MaxCondition) % 0.1f));
        }
    }
}
