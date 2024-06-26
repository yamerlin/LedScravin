﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Formats.Asn1;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using CsvHelper;
using OfficeOpenXml;



namespace BadgeScreen.M2VM.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private TcpClient client;
        private NetworkStream stream;
        private Dictionary<string, string> characterTrad;
        private ObservableCollection<int> ports;
        private string scanStatus;

        public string ScanStatus
        {
            get => scanStatus;
            set
            {
                scanStatus = value;
                OnPropertyChanged();
            }
        }

        public class Messages
        {
            public ObservableCollection<string> messages { get; set; }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        //Fonction qui permet la mise de l'interface du front quand on a un changement.
        //Exemple pour le message de status du scan des ports : pas lancé/en cours/terminé
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        //CONSTRUCTAVOR
        public MainViewModel()
        {
            ExcelPackage.LicenseContext = OfficeOpenXml.LicenseContext.NonCommercial; // Set license POUR LA LIB XLSX
            ports = new ObservableCollection<int>();
            ScanStatus = "Scan pas encore lancé";
            InitMappeur();
        }

        public void ConnectToPort(int port)
        {
            //Créer la nouvelle connexion
            client = new TcpClient("127.0.0.1", port); //On fait que avec une connexion locale

            // Envoie les données via le flux (stream)
            stream = client.GetStream();

            byte[] buffer = Encoding.ASCII.GetBytes(" ");
            stream.Write(buffer, 0, buffer.Length);

            //Se deconnecter pour pas empiéter sur les connections suivantes
            Disconnect();
        }

        //Ajouter un port à la liste de ports
        public void AddPortToPortsList(int port)
        {
            ports.Add(port);
        }

        //Supoprimer un ports de la liste de ports
        public void RemovePortFromList(int port)
        {
            ports.Remove(port);
        }

        //Scanner les ports de manière asynchrone pour pas freeze toute l'app
        public async Task<List<int>> ScanPortsAsync()
        {
            ScanStatus = "Scan en cours...";

            var openPorts = new ConcurrentBag<int>();
            var tasks = new List<Task>();

            for (int port = 1200; port <= 1300; port++)
            {
                int capturedPort = port; // Capture the current port value
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        using TcpClient connectTest = new("127.0.0.1", capturedPort);
                        using NetworkStream ns = connectTest.GetStream();

                        string scanMsg = transformMessage(" ").ToString();
                        byte[] buffer = Encoding.ASCII.GetBytes(scanMsg);
                        await ns.WriteAsync(buffer);

                        await ns.FlushAsync();

                        openPorts.Add(capturedPort);

                        Trace.WriteLine($"Alive connection on port {capturedPort}");
                    }
                    catch
                    {
                        Trace.WriteLine($"Failed to connect to port {capturedPort}");
                    }
                }));
            }

            await Task.WhenAll(tasks);

            ScanStatus = "Scan terminé !";

            return openPorts.ToList();
        }

        //Fonction pour se déconnecter proprement
        public async void Disconnect()
        {
            await stream.FlushAsync();
            stream.Close();
        }

        //Envoyer un message à tous les ports de la liste des ports
        public void SendMessage(string message)
        {
            // Tester si le message est vide
            if (message == "")
            {
                throw new Exception("No message to send");
            }
            Debug.Print("Debut de l'envoie");

            foreach (var portHere in ports)
            {
                //Créer la nouvelle connexion
                client = new TcpClient("127.0.0.1", portHere); //On fait que avec une connexion locale

                //Si le message n'est pas vide, on le passe d'abord en majuscules
                message = message.ToUpper();

                //Ensuite on crée la suite de 0 et 1 que l'on veut envoyer grace à la méthode transformMessage
                string messageBuilder = transformMessage(message).ToString();

                Trace.WriteLine("Message a envoyer ; " + message);
                Trace.WriteLine("Message en byte : " + messageBuilder);


                // Envoie les données via le flux (stream)
                stream = client.GetStream();

                byte[] buffer = Encoding.ASCII.GetBytes(messageBuilder);
                stream.Write(buffer, 0, buffer.Length);

                Disconnect();


                ////Se reconnecter après un envoi
                //ConnectToPort(portHere);
            }


            Debug.Print("Envoye");
        }

        //Codage du message
        private StringBuilder transformMessage(string message)
        {
            int compteur = 1;
            StringBuilder reponse = new StringBuilder();
            string debug;
            int debut = 0;
            int longueur = 5;

            // Construction de la suite de 0 et de 1 à envoyer au badge
            // On la construit ligne par ligne, il y a 11 lignes, donc on fait une boucle qui boucle 11 fois :
            //
            // Sur la première ligne, on append les 5 premiers byte (car debut=0 et longueur=5) de chaque lettre
            // Sur la deuxième ligne, on append les bytes de 5 à 10 (car debut=5 et longueur=5) de chaque lettre
            // Sur la troisième ligne, on append les bytes de 10 à 15 (car debut=10 et longueur=5) de chaque lettre
            // Sur la quatrième ligne, on append les bytes de 15 à 20 (car debut=15 et longueur=5) de chaque lettre
            // etc pour chaque ligne
            //
            // La boucle for permet donc de coder chaque ligne
            // Et dans cette boucle for, la boucle foreach nous permet de coder chaque bout de lettre présent sur cette ligne
            //
            // La variable debut est incrémenter de 5 à la fin de chaque ligne
            // Puisque quand on passe a la ligne suivante on veux chercher le morceau suivant de la lettre
            // Et vu que les lettres font 5 byte de large, le morceau du dessous se situt 5 byte plus loin

            //Pour chaque ligne a coder (11 lignes)
            for (int i = 1; i < 11; i++)
            {
                // Pour chaque lettre a coder (défini par le nombre de lettres présentes dans le message)
                foreach (char lettre in message)
                {
                    // Ajouter le petit bout de lettre à la ligne 
                    reponse.Append(this.characterTrad[lettre.ToString()].Substring(debut, longueur));
                }

                // Si le message à envoyer ne contient pas asser de lettre pour couvrir tout l'écran,
                // il faut couvrir le reste avec des 0
                // Chaque ligne fait 44 pixels

                // Tant que la ligne ne fait pas 44 pixels on ajoute un zero
                while (reponse.Length < 44 * compteur)
                {
                    reponse.Append("0");
                }
                //On multiplie 44 par un compteur qui a compté le nombre d'itération de la boucle
                //Le nombre d'itération de la boucle correspond au numéro de la ligne que l'on est entrain de coder
                //C'est pour cela que 44 est multiplier par le numéro de ligne à laquelle on est
                //C'est parcque que l'on ne veut pas couvrir les lignes du dessous de 0, mais seulement la fin de la ligne actuelle

                compteur++;
                debut += 5;

            }

            return reponse;
        }

        //Initialiser une liste de message depuis un fichier JSON
        public ObservableCollection<string> InitMessages()
        {
            string filePath = "M2VM\\Models\\message.json";


            if (File.Exists(filePath))
            {
                string jsonString = File.ReadAllText(filePath);
                try
                {
                    var messages = JsonSerializer.Deserialize<Messages>(jsonString);

                    if (messages != null && messages.messages != null)
                    {
                        return new ObservableCollection<string>(messages.messages);
                    }
                    else
                    {
                        Debug.WriteLine("La désérialisation a renvoyé un objet null ou une liste de messages null.");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Erreur lors de la désérialisation du fichier JSON : {ex.Message}");
                }

            }
            else
            {
                Debug.WriteLine("Le fichier JSON n'existe pas.");
            }

            return new ObservableCollection<string>();
        }

        //Importer une liste de message depuis un fichier csv
        public void ImportCsv(string filePath, ObservableCollection<string> messages)
        {
            try
            {
                using (var reader = new StreamReader(filePath))
                using (var csv = new CsvReader(reader, CultureInfo.InvariantCulture))
                {
                    while (csv.Read())
                    {
                        var message = csv.GetField<string>(0);
                        if (!string.IsNullOrWhiteSpace(message))
                        {
                            messages.Add(message);
                        }
                    }
                    //FilterMessages("");
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine(ex.Message);
            }
        }

        //Exporter la liste des message dans un fichier csv
        public void ExportCsv(string filePath, ObservableCollection<string> messages)
        {
            try
            {
                using (var writer = new StreamWriter(filePath))
                using (var csv = new CsvWriter(writer, CultureInfo.InvariantCulture))
                {
                    foreach (var message in messages)
                    {
                        csv.WriteField(message);
                        csv.NextRecord();
                    }
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine(ex.Message);
            }
        }

        //Importer une liste de message depuis un fichier excel
        public void ImportExcel(string filePath, ObservableCollection<string> messages)
        {
            try
            {
                using (var package = new ExcelPackage(new FileInfo(filePath)))
                {
                    var worksheet = package.Workbook.Worksheets[0];
                    var rowCount = worksheet.Dimension.Rows;
                    for (int row = 1; row <= rowCount; row++)
                    {
                        var message = worksheet.Cells[row, 1].Value?.ToString();
                        if (!string.IsNullOrWhiteSpace(message))
                        {
                            messages.Add(message);
                        }
                    }
                    //FilterMessages("");
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine(ex.Message);
            }
        }

        //Exporter la liste des message dans un fichier excel
        public void ExportExcel(string filePath, ObservableCollection<string> messages)
        {
            try
            {
                using (var package = new ExcelPackage())
                {
                    var worksheet = package.Workbook.Worksheets.Add("Messages");
                    for (int i = 0; i < messages.Count; i++)
                    {
                        worksheet.Cells[i + 1, 1].Value = messages[i];
                    }
                    package.SaveAs(new FileInfo(filePath));
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine(ex.Message);
            }
        }

        //Le dico des lettres et chiffres
        private void InitMappeur()
        {
            this.characterTrad = new Dictionary<string, string>();

            characterTrad.Add("A", "00000" +
                                   "00000" +
                                   "00000" +
                                   "00110" +
                                   "01001" +
                                   "01111" +
                                   "01001" +
                                   "01001" +
                                   "00000" +
                                   "00000" +
                                   "00000");
            characterTrad.Add("B", "0000000000000000111001001011100100101110000000000000000");
            characterTrad.Add("C", "0000000000000000011101000010000100000111000000000000000");
            characterTrad.Add("D", "0000000000000000111001001010010100101110000000000000000");
            characterTrad.Add("E", "0000000000000000111101000011100100001111000000000000000");
            characterTrad.Add("F", "0000000000000000111101000011100100001000000000000000000");
            characterTrad.Add("G", "0000000000000000011101000010110100100111000000000000000");
            characterTrad.Add("H", "0000000000000000100101001011110100101001000000000000000");
            characterTrad.Add("I", "0000000000000000100001000010000100001000000000000000000");
            characterTrad.Add("J", "0000000000000000000100001000010100100110000000000000000");
            characterTrad.Add("K", "0000000000000000100101010011000101001001000000000000000");
            characterTrad.Add("L", "0000000000000000100001000010000100001110000000000000000");
            characterTrad.Add("M", "0000000000000001000111011101011000110001000000000000000");
            characterTrad.Add("N", "0000000000000000100101101010110100101001000000000000000");
            characterTrad.Add("O", "0000000000000000011001001010010100100110000000000000000");
            characterTrad.Add("P", "0000000000000000111001001011100100001000000000000000000");
            characterTrad.Add("Q", "0000000000000000011001001010010101100111000000000000000");
            characterTrad.Add("R", "0000000000000000111001001011100101001001000000000000000");
            characterTrad.Add("S", "0000000000000000011101000001100000101110000000000000000");
            characterTrad.Add("T", "0000000000000000111000100001000010000100000000000000000");
            characterTrad.Add("U", "0000000000000000100101001010010100101111000000000000000");
            characterTrad.Add("V", "0000000000000000101001010010100101000100000000000000000");
            characterTrad.Add("W", "0000000000000001000110001100011010101010000000000000000");
            characterTrad.Add("X", "0000000000000000101001010001000101001010000000000000000");
            characterTrad.Add("Y", "0000000000000000101001010011100010000100000000000000000");
            characterTrad.Add("Z", "0000000000000000111100001000100010001111000000000000000");
            characterTrad.Add(" ", "0000000000000000000000000000000000000000000000000000000");
            characterTrad.Add("!", "0000000000001000010000100001000000000100000000000000000");

            characterTrad.Add("1", "0000000000000100011001010000100001000010000000000000000");
            characterTrad.Add("2", "0000000000001100100100010000100010001111000000000000000");
            characterTrad.Add("3", "0000000000011110000100111000010000101111000000000000000");
            characterTrad.Add("4", "0000000000010010100101111000010000100001000000000000000");
            characterTrad.Add("5", "0000000000011110100001111000010000101111000000000000000");
            characterTrad.Add("6", "0000000000011110100001111010010100101111000000000000000");
            characterTrad.Add("7", "0000000000011110000100010000100010000100000000000000000");
            characterTrad.Add("8", "0000000000011110100101111010010100101111000000000000000");
            characterTrad.Add("9", "0000000000011110100101111000010000101111000000000000000");
            characterTrad.Add("0", "0000000000011110100101001010010100101111000000000000000");
        }
    }
}
