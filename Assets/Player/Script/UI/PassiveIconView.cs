using BattlePvp.Combat;
using UnityEngine;
using UnityEngine.UI;

namespace BattlePvp.UI
{
    public static class PassiveIconView
    {
        private static readonly Sprite[] Icons=new Sprite[14];
        public static Sprite Icon(int kind)
        {
            if(kind<=0 || kind>=Icons.Length) return null;
            return Icons[kind]!=null ? Icons[kind] : Icons[kind]=Resources.Load<Sprite>("PassiveIcons/"+(PassiveKind)kind);
        }
        public static Image Create(Transform parent,string name,Vector2 position,float size,int kind)
        {
            var root=RoomUiElements.Rect(name,parent,Vector2.one*size,position);
            var background=root.gameObject.AddComponent<OctagonGraphic>(); background.color=new Color(.035f,.09f,.13f,1);
            root.gameObject.AddComponent<Mask>().showMaskGraphic=true;
            var image=RoomUiElements.Rect("Artwork",root,Vector2.one*size,Vector2.zero).gameObject.AddComponent<Image>();
            image.sprite=Icon(kind); image.enabled=image.sprite!=null; image.raycastTarget=false;
            var border=RoomUiElements.Rect("Frame",root,Vector2.one*size,Vector2.zero).gameObject.AddComponent<OctagonGraphic>();
            border.Border=2; border.color=new Color(.25f,.73f,.77f); border.raycastTarget=false;
            return image;
        }
    }

}
