﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DialogSystem.Requirements.Internal;

namespace DialogSystem.Requirements
{
    public abstract class Requirement_NPC : BaseRequirement
    {
        public override DialogRequirementTarget Target
        {
            get { return DialogRequirementTarget.Npc; }
        }

        public override sealed bool Evaluate(IDialogRelevantPlayer player, IDialogRelevantNPC npc, IDialogRelevantWorldInfo worldInfo)
        {
            return Evaluate(npc);
        }

        protected abstract bool Evaluate(IDialogRelevantNPC npc);
    }
}
