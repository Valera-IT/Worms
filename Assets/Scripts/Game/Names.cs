using System.Collections.Generic;

/// Имена червей и команд. Раньше в GameManager лежал массив из двенадцати имён:
/// на четыре команды по восемь червей его не хватало, и лишние становились
/// безымянными «Червями». Теперь имён с запасом, а когда и они кончаются —
/// к имени приписывается номер, как в оригинале («ЖЕНЯ II»).
public static class Names
{
    /// Клички: коротко, чтобы метка над червём не расползалась.
    static readonly string[] Worms =
    {
        "Бивень", "Шустрый", "Пузырь", "Клык", "Гвоздь", "Шкварка",
        "Морковка", "Хруст", "Батон", "Тапок", "Скрепка", "Бублик",
        "Женя", "Рома", "Моша", "Свин", "Кок", "Тюлень", "Штырь", "Дым",
        "Пельмень", "Валет", "Кабан", "Фитиль", "Уголёк", "Кекс",
        "Балда", "Носок", "Ржавый", "Дизель", "Пломбир", "Кувалда",
        "Череп", "Мохнатый", "Сухарь", "Прыщ", "Компот", "Заноза",
        "Штопор", "Бочка", "Тихий", "Лом", "Чайник", "Пуля",
        "Косой", "Батя", "Мятый", "Фугас", "Окурок", "Винтик",
        "Сырок", "Дырокол", "Кирпич", "Гуталин", "Мозоль", "Пятак"
    };

    /// Названия команд — тем же духом, что и клички.
    static readonly string[] Squads =
    {
        "Старики", "Гопники", "Чертилы", "Матросы", "Кроты", "Ежи",
        "Бродяги", "Сапёры", "Ушастые", "Хулиганы", "Пираты", "Мясники",
        "Дачники", "Партизаны", "Бобры", "Викинги"
    };

    static readonly System.Random Shared = new System.Random();

    /// Раздатчик, который не повторяется, пока в мешке есть неиспользованные
    /// имена. Один экземпляр на матч: тогда одинаковых кличек в бою не будет.
    public class Bag
    {
        readonly List<string> _left;
        readonly System.Random _rnd;
        int _round;   // сколько раз мешок опустошался: номер приписки к имени

        public Bag(string[] pool, System.Random rnd)
        {
            _rnd = rnd ?? Shared;
            _left = new List<string>(pool);
            Shuffle();
        }

        void Shuffle()
        {
            for (int i = _left.Count - 1; i > 0; i--)
            {
                int j = _rnd.Next(i + 1);
                (_left[i], _left[j]) = (_left[j], _left[i]);
            }
        }

        public string Next(string[] pool)
        {
            if (_left.Count == 0)
            {
                _left.AddRange(pool);
                Shuffle();
                _round++;
            }
            string name = _left[_left.Count - 1];
            _left.RemoveAt(_left.Count - 1);
            return _round == 0 ? name : name + " " + Roman(_round + 1);
        }

        static string Roman(int n) => n switch
        {
            2 => "II", 3 => "III", 4 => "IV", 5 => "V",
            _ => n.ToString()
        };
    }

    /// Мешок кличек. Сид матча делает состав повторяемым: та же карта — те же черви.
    public static Bag WormBag(int seed) => new Bag(Worms, new System.Random(seed ^ 0x11A3));

    public static string NextWorm(Bag bag) => bag.Next(Worms);

    /// Название команды по номеру: случайное, но без повторов внутри матча.
    public static string[] Teams(int count, int seed)
    {
        var bag = new Bag(Squads, new System.Random(seed ^ 0x5A17));
        var result = new string[count];
        for (int i = 0; i < count; i++) result[i] = bag.Next(Squads);
        return result;
    }

    /// Одно случайное название команды — меню зовёт его для кнопки «другое имя».
    public static string RandomTeam() => Squads[Shared.Next(Squads.Length)];
}
