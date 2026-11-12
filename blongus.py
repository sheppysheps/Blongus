import random
import string
import os
import sys

''' 
blongus.py -Python program that returns a completely nonsense word made from three parts. Uses three line-delimited dictionary text files to generate the word: first.txt, second.txt, third.txt
A "blongus" is an example of this kind of word. We use "nonsense" to mean a word that is not found in any standard English dictionary.
first.txt contains the preamble, the starting act, the calm before the storm. These are the first syllable of the word. For blongus, it would be "bl".
second.txt contains the main body, the meat, the rising act, where the plot begins to thicken and the fall during the hero's journey. For blongus, it would be "on".
third.txt contains the resolution, the final act, the resolution of the conflict, the return of the hero. For blongus, it would be "gus"
Return value: a string.
'''

BASE_DIR = os.path.dirname(os.path.abspath(__file__))

def blongus():
    if random.randint(1, 100) == 1:
        with open(os.path.join(BASE_DIR, "special.txt"), "r", encoding="utf-8") as f:
            special = [line.strip() for line in f if line.strip()]
        return random.choice(special)
    with open(os.path.join(BASE_DIR, "first.txt"), "r", encoding="utf-8") as f:
        first = f.readlines()
    with open(os.path.join(BASE_DIR, "second.txt"), "r", encoding="utf-8") as f:
        second = f.readlines()
    word = random.choice(first).strip() + random.choice(second).strip()
    if random.randint(1, 8) != 1:
        with open(os.path.join(BASE_DIR, "third.txt"), "r", encoding="utf-8") as f:
            third = f.readlines()
        word += random.choice(third).strip()
    return word

def main():
    print(blongus())

if __name__ == "__main__":
    main()