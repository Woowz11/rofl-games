const { Mouse } = require("mechatron");

async function Wait(Seconds){
    await new Promise(R => setTimeout(R, Seconds * 1000))
}

async function Work(){
    console.log("Запуск...")

    await Wait(3)

    console.log("Запуск!")

    const Mouse__ = new Mouse();

    const cx = 960;
    const cy = 540;
    const r = 200;

    let angle = 0;
    let count = 0;
    const circles = 30;

    while(angle < circles * 2 * Math.PI){
        const x = cx + r * Math.cos(angle);
        const y = cy + r * Math.sin(angle);

        Mouse.setPos(x, y);

        count++;
        if(count % 10 === 0){
            console.log(`Угол: ${Math.round(angle * 180 / Math.PI)}`)
        }

        angle += 0.05 * 10;

        await Wait(0.01);
    }
}

Work().catch(e => {
    console.error("ОШИБКА!", e.message);
})