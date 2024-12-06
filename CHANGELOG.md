**1.1.1**

* Updated for Seekers of the Storm.
* Impoved Gesture of the Drowned pickup sprite red outline to better match other items from the same tier.
* Made the Bottled Chaos description correctly show the current config cooldown value instead of hardcoding it in the language file.
* Changed the internal author name from GChinchi to Chinchi. This will generate a new config file; feel free to delete the old one.

**1.1.0**

* Nerfed Bottled Chaos cooldown penalty: 50% -> 20%

  **Developer notes**: The average cooldown of all possible equipment activated by this item is approximately 54 seconds. This means the player trades an extra 27 seconds of cooldown for a randomly activated effect, which does not seem to be a valuable trade. The penalty was originally set to 50% because with a few stacks of Fuel Cells it would already be greatly diminished. However, similar to the extra cooldown argument, such a high value effectively lessened the rate of the benefit the player gained with additional Fuel Cell stacks. Credits to @cbhv4321 for his valuable feedback and suggestions.
* Converted the Bottled Chaos penalty value to a config entry.
* Fixed a bug where The Crowdfunder would not be correctly blacklisted from autocasting.

**1.0.0**

* Release