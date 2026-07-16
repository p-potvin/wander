# A.N.S.W.E.R.S.

## The Alternating, Non Sequential, With Encryption, Restoring System.

Equation to optimize your backups - Patent Pending

10 backups - 10 days
1,2,3,4,5,6,7,8,9,0
insert x
x,1,3,4,5,6,7,8,9,0

## Middleout backup rotation

We should have multiple backups/versions not only on github but locally and on the VPS. The whole documentation would be backed up in 3 or 5 locations: my local machine, greencloud, ovhcloud, google drive, our self hosted vaultwarden server or proton drive, we can even add iCloud and mega to the list. It would use a sync script as well. I have designed a quick backup algorithm that gives us more leeway, more time to catch a critical mistake in our documentation, called The A.N.S.W.E.R.S (The Alternating, Non Sequential, With Encryption, Restoring System.)

backup logic where max backups = 20
frequency = twice a day
the goal being extending the number of days covered by backups as much as possible while keeping a balance of recent backups as well.

What i want to do is:

- once backups have reached N (20), delete a record from the list, alternating between starting at index 0 + skip value
(number of backups that share today’s date + skip modifier, default=1) and starting at N - skip value (number of backups that share today’s date + skip modifier, default=1).
- If the number of backups with current date (this includes the backup not yet inserted) is odd, remove N - skip value, otherwise remove 0 + skip value.
- Increment skip modifier. 
- Repeat until skip value >= N / 2 - 1, then reset skip modifier to default value. 
- Loop is complete, and resets.