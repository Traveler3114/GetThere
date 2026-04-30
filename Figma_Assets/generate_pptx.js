const pptxgen = require('pptxgenjs');
const path = require('path');

let pptx = new pptxgen();
pptx.layout = 'LAYOUT_WIDE';

// MASTER SLIDE with design elements
pptx.defineSlideMaster({
  title: 'GETTHERE_MASTER',
  background: { color: '0A0C10' },
  objects: [
    // Top accent line
    { rect: { x: 0, y: 0, w: '100%', h: 0.05, fill: { color: '009688' } } },
    // Background decorative circles (simulated with large low-opacity shapes)
    { shape: pptx.ShapeType.ellipse, options: { x: -2, y: -2, w: 6, h: 6, fill: { color: '009688', transparency: 90 } } },
    { shape: pptx.ShapeType.ellipse, options: { x: 10, y: 4, w: 5, h: 5, fill: { color: '512BD4', transparency: 92 } } }
  ]
});

// 1. NASLOVNA (Stylized)
let s1 = pptx.addSlide({ masterName: 'GETTHERE_MASTER' });
s1.addShape(pptx.ShapeType.rect, { x: 4.5, y: 2.5, w: 4, h: 1.5, fill: { color: '009688', transparency: 80 }, line: { color: '009688', width: 2 } });
s1.addText('GetThere', { x: 0, y: '35%', w: '100%', align: 'center', fontSize: 80, fontFace: 'Arial Black', color: 'FFFFFF', bold: true });
s1.addText('DIZAJN SUSTAVA MOBILNOSTI', { x: 0, y: '52%', w: '100%', align: 'center', fontSize: 18, color: '009688', bold: true, charSpacing: 4 });

// 2. KONCEPT I VIZIJA (Side-by-side with design)
let s2 = pptx.addSlide({ masterName: 'GETTHERE_MASTER' });
s2.addShape(pptx.ShapeType.rect, { x: 0, y: 0, w: 0.3, h: '100%', fill: { color: '512BD4' } }); // Left vertical bar
s2.addText('Koncept i Vizija', { x: 0.6, y: 0.5, fontSize: 36, color: 'FFFFFF', bold: true });
s2.addText('Digitalni novčanik, trgovina i planiranje u jednom.', { x: 0.6, y: 1.2, fontSize: 18, color: '009688' });
s2.addText('Glavni cilj dizajna je stvoriti pregledno i dosljedno sučelje koje korisnik može lako razumjeti bez obzira na to nalazi li se u profilu, shopu ili mapi.', { x: 0.6, y: 2.2, w: 5.5, fontSize: 18, color: 'E2E8F0' });
s2.addShape(pptx.ShapeType.roundRect, { x: 6.8, y: 1.5, w: 6, h: 4.5, fill: { color: '1A1D23' }, line: { color: '2D3748', width: 1 } });
s2.addText('[VIZUALNI PREGLED]', { x: 6.8, y: 3.5, w: 6, align: 'center', color: '4B5563', fontSize: 14 });

// 3. BOJE (Stylized grid)
let s3 = pptx.addSlide({ masterName: 'GETTHERE_MASTER' });
s3.addText('Vizualni identitet: Boje', { x: 0.5, y: 0.5, fontSize: 32, color: '009688', bold: true });
const colors = [
    { name: 'Teal Accent', hex: '009688', desc: 'Aktivna stanja' },
    { name: 'Deep Purple', hex: '512BD4', desc: 'Brendiranje' },
    { name: 'Amber Glow', hex: 'FFC107', desc: 'Upozorenja' },
    { name: 'Dark Navy', hex: '0A0C10', desc: 'Pozadina' }
];
colors.forEach((c, i) => {
    s3.addShape(pptx.ShapeType.roundRect, { x: 0.5 + (i * 3.1), y: 1.5, w: 2.8, h: 4, fill: { color: '1A1D23' }, rectRadius: 0.2 });
    s3.addShape(pptx.ShapeType.ellipse, { x: 0.9 + (i * 3.1), y: 2.0, w: 2, h: 2, fill: { color: c.hex } });
    s3.addText(c.name, { x: 0.5 + (i * 3.1), y: 4.2, w: 2.8, align: 'center', fontSize: 16, color: 'FFFFFF', bold: true });
    s3.addText(c.desc, { x: 0.5 + (i * 3.1), y: 4.7, w: 2.8, align: 'center', fontSize: 12, color: '94A3B8' });
});

// 4. TIPOGRAFIJA (Visual hierarchy)
let s4 = pptx.addSlide({ masterName: 'GETTHERE_MASTER' });
s4.addText('Vizualni identitet: Tipografija', { x: 0.5, y: 0.5, fontSize: 32, color: '009688', bold: true });
s4.addShape(pptx.ShapeType.rect, { x: 0.5, y: 1.3, w: 12, h: 0.02, fill: { color: '2D3748' } });
s4.addText('Inter font obitelj', { x: 8, y: 0.6, fontSize: 14, color: '4B5563', align: 'right' });

s4.addText('AB', { x: 0.5, y: 1.8, fontSize: 120, color: '009688', opacity: 0.1, bold: true });
s4.addText('Inter Bold (800)', { x: 0.8, y: 2.5, fontSize: 48, color: 'FFFFFF', bold: true });
s4.addText('Inter SemiBold (600)', { x: 0.8, y: 3.8, fontSize: 32, color: '94A3B8', bold: true });
s4.addText('Inter Regular (400)', { x: 0.8, y: 4.8, fontSize: 24, color: '64748B' });

// 5. UX (Process flow)
let s5 = pptx.addSlide({ masterName: 'GETTHERE_MASTER' });
s5.addText('User Experience (UX)', { x: 0.5, y: 0.5, fontSize: 32, color: '009688', bold: true });
s5.addShape(pptx.ShapeType.line, { x: 1, y: 3.5, w: 11, h: 0, line: { color: '009688', width: 2, dashType: 'dash' } });
const steps = ['Istraživanje', 'Persona', 'Flow', 'Testiranje'];
steps.forEach((step, i) => {
    s5.addShape(pptx.ShapeType.ellipse, { x: 1 + (i * 3), y: 3.2, w: 0.6, h: 0.6, fill: { color: '009688' } });
    s5.addText(step, { x: 0.8 + (i * 3), y: 4.0, w: 1, align: 'center', color: 'FFFFFF', bold: true });
});
s5.addText('Fokus dizajna je na osjećaju jasnoće, brzine i povjerenja tijekom korištenja aplikacije.', { x: 0.5, y: 5.5, w: 12, fontSize: 18, align: 'center', color: '94A3B8' });

// 6. LIGHT & DARK (Comparison design)
let s6 = pptx.addSlide({ masterName: 'GETTHERE_MASTER' });
s6.addShape(pptx.ShapeType.rect, { x: 0, y: 0, w: '50%', h: '100%', fill: { color: 'F8F9FA' } }); // Light side
s6.addText('Svijetla tema', { x: 0.5, y: 0.5, fontSize: 24, color: '0A0C10', bold: true });
s6.addText('Tamna tema', { x: 6.8, y: 0.5, fontSize: 24, color: '009688', bold: true });
s6.addShape(pptx.ShapeType.roundRect, { x: 1, y: 1.5, w: 4.5, h: 5, fill: { color: 'FFFFFF' }, line: { color: 'E2E8F0', width: 2 } });
s6.addShape(pptx.ShapeType.roundRect, { x: 7.8, y: 1.5, w: 4.5, h: 5, fill: { color: '1A1D23' }, line: { color: '009688', width: 2 } });

// 7. ZASLONI: PROFIL I SHOP
let s7 = pptx.addSlide({ masterName: 'GETTHERE_MASTER' });
s7.addText('Zasloni: Profil i Shop', { x: 0.5, y: 0.5, fontSize: 32, color: '009688', bold: true });
s7.addShape(pptx.ShapeType.rect, { x: 0.5, y: 1.5, w: 5.5, h: 5, fill: { color: '1A1D23' }, rectRadius: 0.2 });
s7.addShape(pptx.ShapeType.rect, { x: 7.0, y: 1.5, w: 5.5, h: 5, fill: { color: '1A1D23' }, rectRadius: 0.2 });
s7.addText('Wallet & Account', { x: 0.5, y: 6.6, w: 5.5, align: 'center', color: 'FFFFFF', bold: true });
s7.addText('Operator Cards', { x: 7.0, y: 6.6, w: 5.5, align: 'center', color: 'FFFFFF', bold: true });

// 8. ZASLONI: KARTE I MAPA
let s8 = pptx.addSlide({ masterName: 'GETTHERE_MASTER' });
s8.addText('Zasloni: Karte i Mapa', { x: 0.5, y: 0.5, fontSize: 32, color: '009688', bold: true });
s8.addShape(pptx.ShapeType.rect, { x: 0.5, y: 1.5, w: 12, h: 5, fill: { color: '1A1D23' }, line: { color: '512BD4', width: 1 } });
s8.addText('[PREGLED KARATA I MAPE]', { x: 0.5, y: 3.5, w: 12, align: 'center', color: '4B5563' });

// 9. SPECIFIKACIJE (8px grid design)
let s9 = pptx.addSlide({ masterName: 'GETTHERE_MASTER' });
s9.addText('Tehničke Specifikacije', { x: 0.5, y: 0.5, fontSize: 32, color: '009688', bold: true });
for(let i=0; i<10; i++) {
    s9.addShape(pptx.ShapeType.line, { x: 0.5, y: 1.5 + (i * 0.5), w: 12, h: 0, line: { color: '2D3748', width: 0.5 } });
}
s9.addShape(pptx.ShapeType.roundRect, { x: 1, y: 2, w: 3, h: 1, fill: { color: '009688' } });
s9.addText('8px', { x: 4.1, y: 2.3, fontSize: 14, color: '009688', bold: true });
s9.addText('Dosljednost kroz cijelu aplikaciju postignuta je strogim pridržavanjem ritma razmaka.', { x: 1, y: 4.5, w: 11, fontSize: 18, color: 'FFFFFF' });

// 10. ZAKLJUČAK (Grand finale)
let s10 = pptx.addSlide({ masterName: 'GETTHERE_MASTER' });
s10.addShape(pptx.ShapeType.ellipse, { x: 3.5, y: 1.5, w: 6, h: 6, fill: { color: '009688', transparency: 90 } });
s10.addText('GetThere', { x: 0, y: '30%', w: '100%', align: 'center', fontSize: 60, color: 'FFFFFF', bold: true });
s10.addText('MODERNO • STRUKTURIRANO • JEDNOSTAVNO', { x: 0, y: '45%', w: '100%', align: 'center', fontSize: 16, color: '009688', bold: true, charSpacing: 5 });
s10.addText('Hvala na pažnji!', { x: 0, y: '70%', w: '100%', align: 'center', fontSize: 36, color: 'FFFFFF' });

pptx.writeFile({ fileName: path.join(__dirname, 'GetThere_Prezentacija_Short.pptx') })
    .then(fileName => {
        console.log(`Prezentacija ažurirana kao: ${fileName}`);
    })
    .catch(err => {
        console.error('Greška pri spremanju:', err);
    });
