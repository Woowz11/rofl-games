import javax.swing.*;
import java.awt.*;
import java.awt.event.*;
import java.awt.geom.*;
import java.util.ArrayList;
import java.util.Random;

public class Script extends JFrame {
    private Timer timer;
    private int angle = 0;
    private ArrayList<Particle> particles;
    private Random random;
    private Color[] colors = {
        new Color(255, 100, 100),
        new Color(100, 255, 100),
        new Color(100, 100, 255),
        new Color(255, 255, 100),
        new Color(255, 100, 255),
        new Color(100, 255, 255)
    };
    
    public Script() {
        setTitle("Анимированное окно с эффектами");
        setDefaultCloseOperation(JFrame.EXIT_ON_CLOSE);
        setSize(800, 600);
        setLocationRelativeTo(null);
        setResizable(true);
        
        random = new Random();
        particles = new ArrayList<>();
        
        // Создаем частицы
        for (int i = 0; i < 50; i++) {
            particles.add(new Particle());
        }
        
        // Создаем анимационную панель
        AnimationPanel panel = new AnimationPanel();
        add(panel);
        
        // Создаем меню
        createMenuBar();
        
        // Запускаем таймер анимации
        timer = new Timer(16, e -> {
            angle += 2;
            updateParticles();
            repaint();
        });
        timer.start();
        
        // Добавляем обработчик клика мыши
        addMouseListener(new MouseAdapter() {
            @Override
            public void mousePressed(MouseEvent e) {
                // Добавляем взрыв частиц в месте клика
                for (int i = 0; i < 10; i++) {
                    Particle p = new Particle();
                    p.x = e.getX();
                    p.y = e.getY();
                    p.vx = (random.nextFloat() - 0.5f) * 10;
                    p.vy = (random.nextFloat() - 0.5f) * 10;
                    p.life = 100;
                    particles.add(p);
                }
            }
        });
    }
    
    private void createMenuBar() {
        JMenuBar menuBar = new JMenuBar();
        
        JMenu effectsMenu = new JMenu("Эффекты");
        
        JMenuItem speedUp = new JMenuItem("Ускорить");
        speedUp.addActionListener(e -> timer.setDelay(Math.max(5, timer.getDelay() - 5)));
        
        JMenuItem slowDown = new JMenuItem("Замедлить");
        slowDown.addActionListener(e -> timer.setDelay(timer.getDelay() + 5));
        
        JMenuItem addParticles = new JMenuItem("Добавить частицы");
        addParticles.addActionListener(e -> {
            for (int i = 0; i < 20; i++) {
                particles.add(new Particle());
            }
        });
        
        JMenuItem clearParticles = new JMenuItem("Очистить");
        clearParticles.addActionListener(e -> {
            particles.clear();
            for (int i = 0; i < 50; i++) {
                particles.add(new Particle());
            }
        });
        
        effectsMenu.add(speedUp);
        effectsMenu.add(slowDown);
        effectsMenu.addSeparator();
        effectsMenu.add(addParticles);
        effectsMenu.add(clearParticles);
        
        JMenu helpMenu = new JMenu("Справка");
        JMenuItem about = new JMenuItem("О программе");
        about.addActionListener(e -> JOptionPane.showMessageDialog(this,
                "Анимированное окно с частицами\n" +
                "Кликните мышью для создания взрыва!\n" +
                "Используйте меню для управления эффектами.",
                "О программе", JOptionPane.INFORMATION_MESSAGE));
        helpMenu.add(about);
        
        menuBar.add(effectsMenu);
        menuBar.add(helpMenu);
        setJMenuBar(menuBar);
    }
    
    private void updateParticles() {
        particles.removeIf(p -> p.life <= 0);
        
        for (Particle p : particles) {
            p.update();
        }
        
        // Добавляем новые частицы снизу
        if (random.nextInt(10) == 0) {
            Particle p = new Particle();
            p.x = random.nextInt(getWidth());
            p.y = getHeight() + 10;
            p.vy = -random.nextFloat() * 3 - 1;
            particles.add(p);
        }
    }
    
    class AnimationPanel extends JPanel {
        @Override
        protected void paintComponent(Graphics g) {
            super.paintComponent(g);
            Graphics2D g2d = (Graphics2D) g.create();
            
            // Включаем сглаживание
            g2d.setRenderingHint(RenderingHints.KEY_ANTIALIASING, RenderingHints.VALUE_ANTIALIAS_ON);
            
            // Градиентный фон
            GradientPaint gradient = new GradientPaint(
                0, 0, new Color(20, 20, 40),
                0, getHeight(), new Color(60, 20, 80)
            );
            g2d.setPaint(gradient);
            g2d.fillRect(0, 0, getWidth(), getHeight());
            
            // Рисуем вращающиеся кольца в центре
            drawRotatingRings(g2d);
            
            // Рисуем частицы
            drawParticles(g2d);
            
            // Рисуем волны
            drawWaves(g2d);
            
            g2d.dispose();
        }
        
        private void drawRotatingRings(Graphics2D g2d) {
            int centerX = getWidth() / 2;
            int centerY = getHeight() / 2;
            
            g2d.setStroke(new BasicStroke(3, BasicStroke.CAP_ROUND, BasicStroke.JOIN_ROUND));
            
            for (int i = 0; i < 3; i++) {
                g2d.setColor(colors[i % colors.length]);
                
                AffineTransform old = g2d.getTransform();
                g2d.translate(centerX, centerY);
                g2d.rotate(Math.toRadians(angle + i * 120));
                
                int radius = 50 + i * 30;
                g2d.drawOval(-radius, -radius, radius * 2, radius * 2);
                
                // Добавляем точки на кольцах
                for (int j = 0; j < 8; j++) {
                    double pointAngle = Math.toRadians(j * 45);
                    int x = (int) (Math.cos(pointAngle) * radius);
                    int y = (int) (Math.sin(pointAngle) * radius);
                    g2d.fillOval(x - 3, y - 3, 6, 6);
                }
                
                g2d.setTransform(old);
            }
        }
        
        private void drawParticles(Graphics2D g2d) {
            for (Particle p : particles) {
                float alpha = Math.max(0, Math.min(1, p.life / 100.0f));
                Color color = new Color(p.color.getRed(), p.color.getGreen(), p.color.getBlue(), 
                                      (int)(alpha * 255));
                g2d.setColor(color);
                
                int size = (int)(p.size * alpha) + 1;
                g2d.fillOval((int)p.x - size/2, (int)p.y - size/2, size, size);
                
                // Добавляем свечение
                g2d.setColor(new Color(color.getRed(), color.getGreen(), color.getBlue(), 
                                     (int)(alpha * 50)));
                g2d.fillOval((int)p.x - size, (int)p.y - size, size * 2, size * 2);
            }
        }
        
        private void drawWaves(Graphics2D g2d) {
            g2d.setStroke(new BasicStroke(2, BasicStroke.CAP_ROUND, BasicStroke.JOIN_ROUND));
            
            for (int wave = 0; wave < 3; wave++) {
                g2d.setColor(new Color(100, 200, 255, 50 + wave * 20));
                
                GeneralPath path = new GeneralPath();
                boolean first = true;
                
                for (int x = 0; x <= getWidth(); x += 5) {
                    double y = getHeight() - 100 + 
                             Math.sin(Math.toRadians(x * 2 + angle * 3 + wave * 60)) * 30 +
                             Math.sin(Math.toRadians(x * 0.5 + angle + wave * 45)) * 20;
                    
                    if (first) {
                        path.moveTo(x, y);
                        first = false;
                    } else {
                        path.lineTo(x, y);
                    }
                }
                
                g2d.draw(path);
            }
        }
    }
    
    class Particle {
        float x, y, vx, vy;
        Color color;
        int life;
        int size;
        
        public Particle() {
            x = random.nextInt(800);
            y = random.nextInt(600);
            vx = (random.nextFloat() - 0.5f) * 4;
            vy = (random.nextFloat() - 0.5f) * 4;
            color = colors[random.nextInt(colors.length)];
            life = random.nextInt(200) + 100;
            size = random.nextInt(8) + 2;
        }
        
        public void update() {
            x += vx;
            y += vy;
            
            // Гравитация и трение
            vy += 0.02f;
            vx *= 0.999f;
            vy *= 0.999f;
            
            // Отскок от краев
            if (x < 0 || x > 800) vx *= -0.8f;
            if (y < 0 || y > 600) vy *= -0.8f;
            
            x = Math.max(0, Math.min(800, x));
            y = Math.max(0, Math.min(600, y));
            
            life--;
        }
    }
    
    public static void main(String[] args) {
        // Устанавливаем системные свойства для лучшей совместимости с GraalVM
        System.setProperty("java.awt.headless", "false");
        System.setProperty("awt.useSystemAAFontSettings", "on");
        
        SwingUtilities.invokeLater(() -> {
            new Script().setVisible(true);
        });
    }
}