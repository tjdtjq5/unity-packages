using Tjdtjq5.SupaRun;

/// <summary>아이템 종류 정의. 서버 Config에서 관리.</summary>
[Config("inventory")]
public class InventoryItemDef
{
    [PrimaryKey] public string id;          // "sword_01", "potion_hp"
    [NotNull] public string name;           // "화염의 검", "HP 포션"
    public string category;                 // "weapon", "consumable", "material"
    public bool stackable;                  // true=소모품(amount 합산), false=장비(개별 row)
    public int maxStack;                    // 99=포션, 1=장비 (0=무제한)
    public int rarity;                      // 0=일반, 1=희귀, 2=영웅, 3=전설
}
