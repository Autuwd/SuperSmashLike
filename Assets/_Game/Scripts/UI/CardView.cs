using SuperSmashLike.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class CardView : MonoBehaviour
{
    [SerializeField] private Image portraitImage;      // 拖 Portrait 子物体的 Image
    [SerializeField] private TextMeshProUGUI nameText; // 拖 Name 子物体的 TMP

    public void SetData(FighterData fd, System.Action onSelect)
    {
        portraitImage.sprite = fd.portraitIcon;   // 现在改的是 Portrait 了
        nameText.text = fd.fighterName;
        GetComponent<Button>().onClick.AddListener(() => onSelect());
    }
}
