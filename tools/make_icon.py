from pathlib import Path
from PIL import Image, ImageDraw
p = Path(__file__).resolve().parents[1] / "src/Vocabulary/Assets"
p.mkdir(exist_ok=True)
im = Image.new("RGBA", (256,256))
d = ImageDraw.Draw(im)
d.rounded_rectangle((15,14,241,243),radius=54,fill="#235b55")
d.rounded_rectangle((61,43,185,207),radius=13,fill="#f1f2df")
d.rounded_rectangle((73,38,196,195),radius=12,fill="#d0e9dc")
d.rectangle((87,38,93,195),fill="#81aa98")
d.polygon([(140,38),(164,38),(164,97),(152,87),(140,97)],fill="#356c60")
for y,w in [(135,54),(147,40)]: d.rounded_rectangle((111,y,111+w,y+4),radius=2,fill="#5e8b7a")
im.save(p / "Vocabulary.ico",sizes=[(i,i) for i in (16,20,24,32,40,48,64,128,256)])

